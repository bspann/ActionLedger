using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ActionLedger.Domain.Actions;
using ActionLedger.Domain.Common;
using ActionLedger.Domain.Meetings;

namespace ActionLedger.Domain.Extraction;

/// <summary>
/// AD-3 — one attempt to turn a Meeting's notes into proposals. It owns the
/// <see cref="ProposedAction"/> list, and it is the only thing that mints one.
/// </summary>
/// <remarks>
/// <para>
/// AD-5 / ADR-002 — the run records <see cref="MeetingNotesId"/> and a SHA-256 of the notes text it
/// read, so two runs against the same Meeting can be told apart or matched without re-reading
/// 50,000 characters. Any number of runs may exist per Meeting, and an earlier run's rows are never
/// touched by a later one.
/// </para>
/// <para>
/// AD-11 — a run is persisted whatever its outcome. A provider that returned nonsense is not an
/// HTTP error: it is a row with <see cref="ExtractionOutcome.Failed"/>, a
/// <see cref="FailureReason"/> a human is shown, and no proposals. That is why
/// <see cref="Start"/> receives the finished metrics and the outcome rather than a later
/// <c>Complete</c> call: AD-20 allows one commit, and <c>ARCHITECTURE.md:211</c> puts
/// <c>Start(...).AddProposals(...)</c> after the extractor returns, so a two-phase start would
/// either write the row twice or leave a half-populated aggregate in memory for no benefit.
/// </para>
/// <para>
/// AD-20 — the run carries no concurrency token. The four token-carrying roots are
/// <c>ProposedAction</c>, <c>TrackedAction</c>, <c>Meeting</c> and <c>OutboxMessage</c>, because
/// those are the rows a second actor can race. A run is written once and never updated.
/// </para>
/// </remarks>
public sealed class ExtractionRun : AggregateRoot
{
    /// <summary>The longest failure reason a run stores. A longer one is clipped to this.</summary>
    public const int FailureReasonMaxLength = 2_000;

    /// <summary>
    /// The one options object the AiProposal revision's new value is written with. Cached because
    /// <see cref="JsonSerializerOptions"/> is expensive to build and this runs once per proposal.
    /// </summary>
    /// <remarks>
    /// camelCase to match every other JSON this system publishes, unindented because the value is
    /// stored rather than read by eye, and nulls kept: <c>suggestedDueDate: null</c> is the fact
    /// that the notes stated no date, and omitting it would make "no date" and "no field"
    /// indistinguishable. <see cref="DateOnly"/> serializes as <c>yyyy-MM-dd</c> by default, which
    /// is the Consistency Conventions' date format.
    /// </remarks>
    // The relaxed encoder keeps non-ASCII text and characters such as ' and & literal in the
    // stored audit record; the default escapes them as \uXXXX, which is valid JSON but no longer
    // reads, or searches, as the proposal the AI made. The value is stored, never embedded in HTML.
    private static readonly JsonSerializerOptions RevisionJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly List<ProposedAction> _proposals = [];

    /// <summary>
    /// Whether <see cref="AddProposals"/> has run. Kept beside the list because a succeeded run
    /// that kept nothing still had its proposals added, and "the list is empty" cannot tell that
    /// apart from "nothing has been added yet".
    /// </summary>
    private bool _proposalsAdded;

    private ExtractionRun(
        Guid meetingId,
        Guid meetingNotesId,
        string notesSha256,
        Guid startedByUserId,
        ExtractionRunMetadata metadata,
        ExtractionOutcome outcome,
        string? failureReason,
        IReadOnlyList<string> warnings)
    {
        MeetingId = meetingId;
        MeetingNotesId = meetingNotesId;
        NotesSha256 = notesSha256;
        StartedByUserId = startedByUserId;
        Provider = metadata.Provider;
        Model = metadata.Model;
        PromptVersion = metadata.PromptVersion;
        SchemaVersion = metadata.SchemaVersion;
        StartedAt = metadata.StartedAt;
        DurationMs = metadata.DurationMs;
        InputTokens = metadata.InputTokens;
        OutputTokens = metadata.OutputTokens;
        Outcome = outcome;
        FailureReason = failureReason;
        Warnings = warnings;
    }

    /// <summary>Rehydration constructor for EF Core.</summary>
    /// <remarks>
    /// <see cref="_proposalsAdded"/> is set here because a run that is being loaded has already
    /// been through <see cref="AddProposals"/> — it was persisted, so the one call it gets has
    /// happened. The field is not a mapped column, so without this a rehydrated run would fall
    /// back to the <c>_proposals.Count &gt; 0</c> check, which is exactly the check that cannot
    /// tell a Succeeded run that kept nothing from one that has not run yet.
    /// </remarks>
    private ExtractionRun()
    {
        NotesSha256 = string.Empty;
        Provider = string.Empty;
        Model = string.Empty;
        PromptVersion = string.Empty;
        SchemaVersion = string.Empty;
        Warnings = [];
        _proposalsAdded = true;
    }

    /// <summary>The Meeting this run read. A Meeting may have any number of runs (AD-5).</summary>
    public Guid MeetingId { get; private set; }

    /// <summary>The exact notes row this run read (AD-5).</summary>
    public Guid MeetingNotesId { get; private set; }

    /// <summary>
    /// Lower-case hex SHA-256 of the notes text this run read — copied from
    /// <see cref="MeetingNotes.Sha256"/>, never recomputed, so "the run read those bytes" is a
    /// comparison rather than a claim.
    /// </summary>
    public string NotesSha256 { get; private set; }

    /// <summary>The User who started the run, from the token's <c>sub</c> claim (AD-12).</summary>
    public Guid StartedByUserId { get; private set; }

    /// <summary>AD-6 — the configured <c>Ai:Provider</c>.</summary>
    public string Provider { get; private set; }

    /// <summary>AD-6 — what the active provider reported as its model.</summary>
    public string Model { get; private set; }

    /// <summary>AD-6 — the prompt revision the catalog resolved.</summary>
    public string PromptVersion { get; private set; }

    /// <summary>AD-6 — the committed extraction schema's own top-level <c>version</c>.</summary>
    public string SchemaVersion { get; private set; }

    /// <summary>AD-6 — when the first provider call began, from the extractor, not <c>IClock</c>.</summary>
    public DateTimeOffset StartedAt { get; private set; }

    /// <summary>AD-6 — milliseconds across every attempt, from the extractor.</summary>
    public int DurationMs { get; private set; }

    /// <summary>AD-6 — prompt tokens. <c>0</c> for the Fake provider, never null (FR-6).</summary>
    public int InputTokens { get; private set; }

    /// <summary>AD-6 — completion tokens. <c>0</c> for the Fake provider, never null (FR-6).</summary>
    public int OutputTokens { get; private set; }

    /// <summary>AD-6 — how the run ended. Both values are ordinary outcomes.</summary>
    public ExtractionOutcome Outcome { get; private set; }

    /// <summary>
    /// AD-6 — why the run failed, or <c>null</c> when it did not. Stored as the extractor wrote
    /// it, trimmed, and clipped to <see cref="FailureReasonMaxLength"/> characters. The pairing with
    /// <see cref="Outcome"/> is enforced: Failed always has one, Succeeded never does.
    /// </summary>
    public string? FailureReason { get; private set; }

    /// <summary>
    /// AD-6 — one warning per proposal the excerpt verifier dropped, each carrying the dropped
    /// excerpt verbatim. Empty on a clean run, never <c>null</c>. Dropped proposals are not rows:
    /// they survive only as these warnings.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; private set; }

    /// <summary>
    /// The proposals this run produced, in the order the provider returned them. Empty on a Failed
    /// run, and empty until <see cref="AddProposals"/> has run.
    /// </summary>
    public IReadOnlyList<ProposedAction> Proposals => _proposals;

    /// <summary>
    /// Starts — and finishes — a run. The metrics and the outcome are already known because the
    /// extractor has already returned (AD-11, <c>ARCHITECTURE.md:211</c>).
    /// </summary>
    /// <param name="meetingId">The Meeting whose notes were read.</param>
    /// <param name="meetingNotesId">The exact notes row that was read (AD-5).</param>
    /// <param name="notesSha256">The notes' own SHA-256, copied rather than recomputed (AD-5).</param>
    /// <param name="startedByUserId">The acting User's id, from <c>ICurrentUser</c> (AD-12).</param>
    /// <param name="metadata">The AD-6 measurements of the provider call.</param>
    /// <param name="outcome">Whether the extractor produced a validated answer.</param>
    /// <param name="failureReason">Required when <paramref name="outcome"/> is Failed; refused otherwise.</param>
    /// <param name="warnings">One per dropped proposal. <c>null</c> means none.</param>
    /// <exception cref="DomainRuleException">
    /// An id is empty, the hash is not a SHA-256, the metadata is out of range, or the outcome and
    /// the failure reason disagree.
    /// </exception>
    public static ExtractionRun Start(
        Guid meetingId,
        Guid meetingNotesId,
        string notesSha256,
        Guid startedByUserId,
        ExtractionRunMetadata metadata,
        ExtractionOutcome outcome,
        string? failureReason,
        IReadOnlyList<string>? warnings)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        return new ExtractionRun(
            RequireId(meetingId, "Meeting"),
            RequireId(meetingNotesId, "Meeting Notes"),
            RequireSha256(notesSha256),
            RequireId(startedByUserId, "User who started it"),
            metadata.Validated(),
            outcome,
            RequireReasonMatchesOutcome(outcome, failureReason),
            NormalizeWarnings(warnings));
    }

    /// <summary>
    /// AD-3 / AD-7 — mints this run's proposals and the AiProposal revision that records each one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The revisions are returned rather than held, because AD-7 gives <see cref="ActionRevision"/>
    /// its own persistence root with no navigation from any aggregate. Returning them is how the
    /// handler hands them to a repository the aggregate must not know about.
    /// </para>
    /// <para>
    /// Every revision this call produces carries the same <paramref name="now"/>, so a single call's
    /// rows never straddle a second boundary.
    /// </para>
    /// </remarks>
    /// <param name="drafts">The kept proposals, in the order the provider returned them.</param>
    /// <param name="now">The AD-7 shared instant, read once per request from <c>IClock</c>.</param>
    /// <returns>One AiProposal revision per draft, in the same order.</returns>
    /// <exception cref="DomainRuleException">
    /// This run already has its proposals, or it failed — a failed run has no answer to propose.
    /// </exception>
    public IReadOnlyList<ActionRevision> AddProposals(IReadOnlyList<ProposedActionDraft> drafts, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(drafts);

        if (Outcome == ExtractionOutcome.Failed)
        {
            throw new DomainRuleException("A failed Extraction Run has no proposals; it carries a failure reason instead.");
        }

        if (_proposalsAdded || _proposals.Count > 0)
        {
            throw new DomainRuleException("An Extraction Run's proposals are added once. Start another run instead.");
        }

        DateTimeOffset stamped = now.ToUniversalTime();

        // AD-7 — one instant for every revision this call produces, and a brand-new target's first
        // revision is sequence 1.
        List<ActionRevision> revisions = [];

        for (int ordinal = 0; ordinal < drafts.Count; ordinal++)
        {
            ProposedActionDraft draft = drafts[ordinal];

            ProposedAction proposal = new(Id, ordinal, draft);
            _proposals.Add(proposal);
            revisions.Add(ActionRevision.AiProposal(proposal.Id, ToJson(draft), stamped));
        }

        _proposalsAdded = true;

        return revisions;
    }

    /// <summary>
    /// FR-21 (<c>prd.md:321</c>) — the proposal as JSON, carrying exactly <c>description</c>,
    /// <c>suggestedOwner</c>, <c>suggestedDueDate</c>, <c>confidence</c> and <c>sourceExcerpt</c>.
    /// </summary>
    /// <remarks>
    /// The draft is serialized rather than the entity, so <c>Id</c>, <c>Ordinal</c> and
    /// <c>ReviewState</c> stay out of a record FR-21 defines by its five fields.
    /// <c>System.Text.Json</c> ships in the <c>net10.0</c> shared framework, so Domain serializing
    /// needs no package and Rule 1's zero-reference assertion still holds.
    /// </remarks>
    private static string ToJson(ProposedActionDraft draft) => JsonSerializer.Serialize(draft, RevisionJson);

    private static Guid RequireId(Guid id, string what) =>
        id == Guid.Empty
            ? throw new DomainRuleException($"An Extraction Run must record the {what}.")
            : id;

    private static string RequireSha256(string notesSha256)
    {
        string value = (notesSha256 ?? string.Empty).Trim();

        return value.Length == MeetingNotes.Sha256Length
            ? value
            : throw new DomainRuleException(
                $"An Extraction Run's notes hash must be a {MeetingNotes.Sha256Length}-character SHA-256 digest.");
    }

    /// <summary>
    /// AD-6 keeps <c>Outcome</c> and <c>FailureReason</c> in step. A Failed run with nothing to say
    /// leaves the UI with "Run again" and no reason; a Succeeded run carrying one is a row two
    /// readers would read two ways.
    /// </summary>
    private static string? RequireReasonMatchesOutcome(ExtractionOutcome outcome, string? failureReason)
    {
        string? reason = string.IsNullOrWhiteSpace(failureReason) ? null : failureReason.Trim();

        if (outcome == ExtractionOutcome.Failed && reason is null)
        {
            throw new DomainRuleException("A failed Extraction Run must record why it failed.");
        }

        if (outcome == ExtractionOutcome.Succeeded && reason is not null)
        {
            throw new DomainRuleException("A succeeded Extraction Run cannot carry a failure reason.");
        }

        // The one place this aggregate truncates rather than refuses. The reason is written by our
        // own extractor and can carry an SDK exception's message, whose length nothing upstream
        // bounds. Throwing here would turn a failed run into a 500 — precisely the outcome AD-11
        // exists to prevent — so an over-long reason is clipped to what the column holds.
        return reason is not null && reason.Length > FailureReasonMaxLength
            ? Clip(reason)
            : reason;
    }

    /// <summary>
    /// Cuts a reason to <see cref="FailureReasonMaxLength"/> without splitting a surrogate pair.
    /// </summary>
    /// <remarks>
    /// A plain <c>reason[..FailureReasonMaxLength]</c> can land between the two halves of an
    /// astral character — an emoji in a provider's error message is enough — and store a lone
    /// high surrogate. That is not well-formed text: it round-trips through PostgreSQL as a
    /// replacement character and would make the reason a human is shown end in mojibake.
    /// So when the last character kept is a high surrogate, the cut backs off by one.
    /// </remarks>
    private static string Clip(string reason)
    {
        int length = FailureReasonMaxLength;

        if (char.IsHighSurrogate(reason[length - 1]))
        {
            length--;
        }

        return reason[..length];
    }

    private static IReadOnlyList<string> NormalizeWarnings(IReadOnlyList<string>? warnings)
    {
        if (warnings is null || warnings.Count == 0)
        {
            return [];
        }

        foreach (string warning in warnings)
        {
            if (string.IsNullOrWhiteSpace(warning))
            {
                throw new DomainRuleException("An Extraction Run's warning cannot be blank.");
            }
        }

        return [.. warnings];
    }
}
