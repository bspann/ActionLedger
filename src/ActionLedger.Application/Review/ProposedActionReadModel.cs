using ActionLedger.Application.Abstractions;
using ActionLedger.Application.Ai;
using ActionLedger.Application.Users;
using ActionLedger.Domain.Actions;
using ActionLedger.Domain.Extraction;
using ActionLedger.Domain.Meetings;
using ActionLedger.Domain.Users;

namespace ActionLedger.Application.Review;

/// <summary>
/// AD-9 / AD-15 — the single query class that produces <see cref="ProposedActionDto"/>, for Run
/// Detail and the Review Screen. It is the one place <c>isLowConfidence</c>,
/// <c>suggestedOwnerUserId</c> and the excerpt span are computed.
/// </summary>
/// <remarks>
/// <para>
/// The query projects the persisted columns, materializes, and only then sets the two derived
/// values. That is not an oversight: <see cref="OwnerResolver.Match"/> is pure C# with
/// <see cref="StringComparer.OrdinalIgnoreCase"/> semantics EF cannot translate, and
/// <see cref="IExtractionSettings"/> is an Application port with no SQL meaning. Doing it here,
/// once, is what stops Run Detail and the Review Screen ever disagreeing.
/// </para>
/// <para>
/// A non-empty run costs four more reads, each one query and each skipped when it has nothing to
/// ask for: the roster, the run's notes text, the Tracked Actions of its approved and edited
/// proposals, and the display names of every decider and owner. The last reads all users, system
/// users included, because the Seed identity decides seeded proposals and its name is still who
/// decided.
/// </para>
/// <para>
/// Ordering is by <c>Ordinal</c> and nothing else. <see cref="IReadDb"/> is untracked and a query
/// planner may return rows in any order it likes, so "AI order" has to be asked for.
/// </para>
/// </remarks>
public sealed class ProposedActionReadModel(IReadDb readDb, IExtractionSettings settings)
{
    /// <summary>One run's proposals, in AI order, with every derived value set.</summary>
    /// <param name="extractionRunId">The run whose proposals to read.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    public async Task<IReadOnlyList<ProposedActionDto>> ForRunAsync(
        Guid extractionRunId,
        CancellationToken cancellationToken = default)
    {
        IQueryable<ProposedActionDto> query = readDb.Query<ProposedAction>()
            .Where(proposal => proposal.ExtractionRunId == extractionRunId)
            .OrderBy(proposal => proposal.Ordinal)
            .Select(proposal => new ProposedActionDto(
                proposal.Id,
                proposal.Ordinal,
                proposal.Description,
                proposal.SuggestedOwner,
                proposal.SuggestedDueDate,
                proposal.Confidence,
                proposal.SourceExcerpt,
                // Placeholders. The derived values are set below, in memory, for the reason in the
                // remarks; the Tracked Action's values and the names come from their own reads.
                false,
                null,
                proposal.ReviewState,
                null,
                proposal.DecidedByUserId,
                null,
                proposal.DecidedAt,
                proposal.RejectionReason,
                null,
                null,
                null,
                null,
                null,
                null,
                null));

        IReadOnlyList<ProposedActionDto> proposals = await readDb.ToListAsync(query, cancellationToken);

        if (proposals.Count == 0)
        {
            // A failed run has none, and a succeeded run may have kept nothing. Neither is worth a
            // roster query.
            return [];
        }

        // Non-system users only, read the way the decision handler reads them (AD-9).
        IReadOnlyList<UserSummaryDto> roster = await OwnerRoster.ReadAsync(readDb, cancellationToken);

        string? notes = await ReadNotesAsync(extractionRunId, cancellationToken);

        Dictionary<Guid, TrackedRow> tracked = await ReadTrackedAsync(proposals, cancellationToken);

        Dictionary<Guid, string> names = await ReadNamesAsync(
            [
                .. proposals.Select(proposal => proposal.DecidedByUserId),
                .. tracked.Values.Select(row => row.OwnerUserId),
            ],
            cancellationToken);

        double threshold = settings.LowConfidenceThreshold;

        return
        [
            .. proposals.Select(proposal =>
            {
                Guid? suggestedOwnerUserId = OwnerResolver.Match(proposal.SuggestedOwner, roster);
                TrackedRow? action = tracked.GetValueOrDefault(proposal.Id);
                ExcerptSpan? span = ExcerptLocator.Locate(proposal.SourceExcerpt, notes);

                return proposal with
                {
                    // Strictly below (AD-15). A proposal exactly at the threshold is not flagged:
                    // the threshold is the first confidence that is *not* low.
                    IsLowConfidence = proposal.Confidence < threshold,
                    SuggestedOwnerUserId = suggestedOwnerUserId,
                    SuggestedOwnerDisplayName = roster.FirstOrDefault(user => user.Id == suggestedOwnerUserId)?.DisplayName,
                    DecidedByDisplayName = NameOf(proposal.DecidedByUserId, names),
                    TrackedActionId = action?.Id,
                    DecidedDescription = action?.Description,
                    DecidedOwnerUserId = action?.OwnerUserId,
                    DecidedOwnerDisplayName = NameOf(action?.OwnerUserId, names),
                    DecidedDueDate = action?.DueDate,
                    ExcerptStart = span?.Start,
                    ExcerptLength = span?.Length,
                };
            }),
        ];
    }

    /// <summary>
    /// The text of the notes the run read, or <c>null</c> if there is none to read. Notes are owned
    /// by their Meeting and have no set of their own, so they are reached through it.
    /// </summary>
    private async Task<string?> ReadNotesAsync(Guid extractionRunId, CancellationToken cancellationToken)
    {
        // Hoisted for the reason RunsQueries.ListForMeetingAsync gives: composing against a local is
        // translatable, while calling readDb.Query<T>() inside the lambda is not.
        IQueryable<ExtractionRun> run = readDb.Query<ExtractionRun>().Where(candidate => candidate.Id == extractionRunId);

        IReadOnlyList<string> found = await readDb.ToListAsync(
            readDb.Query<Meeting>()
                .Where(meeting => meeting.Notes != null && run.Any(candidate => candidate.MeetingNotesId == meeting.Notes.Id))
                .Select(meeting => meeting.Notes!.Text),
            cancellationToken);

        return found.Count == 0 ? null : found[0];
    }

    /// <summary>The Tracked Actions of the run's approved and edited proposals, by proposal id.</summary>
    private async Task<Dictionary<Guid, TrackedRow>> ReadTrackedAsync(
        IReadOnlyList<ProposedActionDto> proposals,
        CancellationToken cancellationToken)
    {
        Guid[] accepted =
        [
            .. proposals
                .Where(proposal => proposal.ReviewState is ReviewState.Approved or ReviewState.Edited)
                .Select(proposal => proposal.Id),
        ];

        if (accepted.Length == 0)
        {
            return [];
        }

        IReadOnlyList<TrackedRow> rows = await readDb.ToListAsync(
            readDb.Query<TrackedAction>()
                .Where(action => accepted.Contains(action.ProposedActionId))
                .Select(action => new TrackedRow(
                    action.Id,
                    action.ProposedActionId,
                    action.Description,
                    action.OwnerUserId,
                    action.DueDate)),
            cancellationToken);

        return rows.ToDictionary(row => row.ProposedActionId);
    }

    /// <summary>Display names by User id, from every User — system users included.</summary>
    private async Task<Dictionary<Guid, string>> ReadNamesAsync(
        IEnumerable<Guid?> userIds,
        CancellationToken cancellationToken)
    {
        Guid[] ids = [.. userIds.OfType<Guid>().Distinct()];

        if (ids.Length == 0)
        {
            return [];
        }

        IReadOnlyList<UserName> rows = await readDb.ToListAsync(
            readDb.Query<User>()
                .Where(user => ids.Contains(user.Id))
                .Select(user => new UserName(user.Id, user.DisplayName)),
            cancellationToken);

        return rows.ToDictionary(row => row.Id, row => row.DisplayName);
    }

    private static string? NameOf(Guid? userId, Dictionary<Guid, string> names) =>
        userId is { } id ? names.GetValueOrDefault(id) : null;

    /// <summary>The Tracked Action columns this shape publishes, projected in SQL.</summary>
    private sealed record TrackedRow(
        Guid Id,
        Guid ProposedActionId,
        string Description,
        Guid? OwnerUserId,
        DateOnly? DueDate);

    /// <summary>One User's id and display name, projected in SQL.</summary>
    private sealed record UserName(Guid Id, string DisplayName);
}
