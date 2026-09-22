using ActionLedger.Application.Abstractions;
using ActionLedger.Application.Ai;
using ActionLedger.Domain.Extraction;
using ActionLedger.Domain.Meetings;
using Microsoft.Extensions.Logging;

namespace ActionLedger.Application.Extraction;

/// <summary>
/// AD-11 / AD-20 — the one write use case behind <c>POST /api/v1/meetings/{id}/runs</c>. It runs
/// the extraction synchronously inside the request (FR-4) and persists the run — Succeeded or
/// Failed — together with its proposals and their AiProposal revisions, in one commit.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A failed extraction is not an error here.</strong> <c>IActionExtractor</c> does not
/// throw for a provider or validation failure; it answers with
/// <c>ExtractionResult.Failed(reason, metrics)</c>, and this handler persists that as a run with
/// <see cref="ExtractionOutcome.Failed"/>, its reason as the extractor wrote it — trimmed, and
/// clipped to 2,000 characters — and no proposals. The controller answers 201 either way (AD-11,
/// Consistency Conventions, Errors row).
/// </para>
/// <para>
/// The two instants come from two different places, deliberately.
/// <c>ExtractionMetrics.StartedAt</c> and <c>DurationMs</c> are the extractor's own measurement —
/// it is the only code that sees both ends of the provider call. <see cref="IClock.UtcNow"/> is
/// read once, immediately after that call returns, and is used only as the AD-7 shared <c>now</c>
/// stamped on every revision the aggregate mints. Reading it any earlier would make a run's audit
/// trail claim the proposals were recorded before the run began.
/// </para>
/// <para>
/// There is no command type and no request body: the method takes the Meeting id from the route and
/// the actor from <see cref="ICurrentUser"/> (AD-12). <c>ActorIntegrityTests</c> bans an actor on a
/// command, and a command with nothing else on it would be a shape whose only member is the one
/// thing it may not carry.
/// </para>
/// </remarks>
public sealed class RunExtractionHandler(
    IMeetingRepository meetings,
    IExtractionRunRepository runs,
    IActionRevisionRepository revisions,
    IActionExtractor extractor,
    ICurrentUser currentUser,
    IClock clock,
    IUnitOfWork unitOfWork,
    ILogger<RunExtractionHandler> logger)
{
    /// <summary>
    /// The <c>ExtractionRunCompleted</c> message template (NFR-4, spine Logging row). It names the
    /// run, the provider, the model, the prompt version, the duration, both token counts and the
    /// outcome — and no notes text, description, excerpt or failure reason. The correlation id is
    /// ambient through Serilog's <c>LogContext</c> and is never a parameter here.
    /// </summary>
    private const string RunCompletedTemplate =
        "ExtractionRunCompleted run {ExtractionRunId} meeting {MeetingId} provider {Provider} "
        + "model {Model} promptVersion {PromptVersion} durationMs {DurationMs} "
        + "inputTokens {InputTokens} outputTokens {OutputTokens} outcome {Outcome}";

    /// <summary>Runs extraction against a Meeting's notes and persists the whole run.</summary>
    /// <param name="meetingId">The Meeting from the route.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <exception cref="NotFoundException">No Meeting has that id.</exception>
    /// <exception cref="ValidationFailedException">The Meeting has no notes to read (FR-4).</exception>
    /// <exception cref="OperationCanceledException">The caller abandoned the request mid-call.</exception>
    public async Task<RunDto> HandleAsync(Guid meetingId, CancellationToken cancellationToken = default)
    {
        Meeting meeting = await meetings.FindByIdAsync(meetingId, cancellationToken)
            ?? throw new NotFoundException("Meeting", meetingId);

        // FR-4 — there is nothing to extract from. A 400, not a 404: the Meeting is really there,
        // and the same request would succeed once notes are attached.
        MeetingNotes notes = meeting.Notes
            ?? throw new ValidationFailedException(
                "This meeting has no notes. Add notes before running extraction.");

        // FR-4 — the notes text and the Meeting date, and nothing else about the Meeting.
        ExtractionResult result = await extractor.ExtractAsync(
            new ExtractionRequest(notes.Text, meeting.MeetingDate),
            cancellationToken);

        // AD-15 — read once per request, and used only as the AD-7 shared revision instant. Read
        // *after* the provider call, because it stamps when the proposals were recorded and they do
        // not exist until the extractor returns. Read before the call it would sit earlier than the
        // run's own StartedAt — by up to the whole 180-second ceiling once a real provider is wired
        // — and the audit trail would claim the proposals were recorded before the run began.
        DateTimeOffset now = clock.UtcNow;

        ExtractionRun run = ExtractionRun.Start(
            meeting.Id,
            notes.Id,
            // AD-5 — copied from the notes, never recomputed.
            notes.Sha256,
            currentUser.UserId,
            MetadataFrom(result.Metrics),
            result.IsSucceeded ? ExtractionOutcome.Succeeded : ExtractionOutcome.Failed,
            result.FailureReason,
            // One warning per dropped proposal. The dropped proposals themselves are not rows.
            result.Warnings);

        if (result.IsSucceeded)
        {
            // AD-7 — the aggregate mints the revisions and hands them back, because the revision
            // root has no navigation from any aggregate and the aggregate must not know a
            // repository.
            //
            // Called for an empty Kept too, deliberately. A succeeded run that kept nothing still
            // had its proposals added, and skipping the call would leave the aggregate's once-only
            // flag unset — which is the exact state that flag exists to tell apart from "nothing
            // has been added yet". AddRange of an empty list stages nothing.
            revisions.AddRange(run.AddProposals(DraftsFrom(result.Kept), now));
        }

        runs.Add(run);

        // AD-20 — exactly one commit, covering the run, its proposals, and its revisions.
        await unitOfWork.CommitAsync(cancellationToken);

        // NFR-4 — one event per *persisted* run, so it goes after the commit. Logged before it, a
        // commit that threw would still have announced a completed run that has no row, and the
        // one line an operator counts runs by would disagree with the table.
        logger.LogInformation(
            RunCompletedTemplate,
            run.Id,
            run.MeetingId,
            run.Provider,
            run.Model,
            run.PromptVersion,
            run.DurationMs,
            run.InputTokens,
            run.OutputTokens,
            run.Outcome);

        return new RunDto(run.Id, run.Outcome);
    }

    /// <summary>
    /// The one place <c>ExtractionMetrics</c> becomes <see cref="ExtractionRunMetadata"/>. AD-1
    /// keeps Domain from seeing this ring at all, so the two are separate types by construction and
    /// this map is where they meet.
    /// </summary>
    private static ExtractionRunMetadata MetadataFrom(ExtractionMetrics metrics) =>
        new(
            metrics.Provider,
            metrics.Model,
            metrics.PromptVersion,
            metrics.SchemaVersion,
            // AD-6 — the extractor's own measurement, not IClock's.
            metrics.StartedAt,
            metrics.DurationMs,
            metrics.InputTokens,
            metrics.OutputTokens);

    /// <summary>
    /// The kept proposals, in provider order, as the aggregate's input shape. Order is load-bearing:
    /// <c>AddProposals</c> assigns ordinals from the list's own indices.
    /// </summary>
    private static IReadOnlyList<ProposedActionDraft> DraftsFrom(IReadOnlyList<ExtractedProposal> kept) =>
    [
        .. kept.Select(proposal => new ProposedActionDraft(
            proposal.Description,
            proposal.SuggestedOwner,
            proposal.SuggestedDueDate,
            proposal.Confidence,
            proposal.SourceExcerpt)),
    ];
}
