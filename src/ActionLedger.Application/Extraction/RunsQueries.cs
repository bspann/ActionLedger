using ActionLedger.Application.Abstractions;
using ActionLedger.Application.Review;
using ActionLedger.Domain.Extraction;
using ActionLedger.Domain.Meetings;

namespace ActionLedger.Application.Extraction;

/// <summary>
/// AD-2 — reads are per-feature query classes over <see cref="IReadDb"/>. This one serves
/// <c>GET /api/v1/runs/{id}</c>, which FR-9 renders as Run Detail, and
/// <c>GET /api/v1/meetings/{id}/runs</c>, which Meeting Detail renders as its run list.
/// </summary>
/// <remarks>
/// The proposals come from <see cref="ProposedActionReadModel"/> rather than from a projection
/// here, because AD-9 makes that class the single producer of <see cref="ProposedActionDto"/> for
/// both this screen and the Epic 3 Review Screen. Composing it is what keeps the two from ever
/// disagreeing about a flag.
/// </remarks>
public sealed class RunsQueries(IReadDb readDb, ProposedActionReadModel proposals)
{
    /// <summary>One run, with every AD-6 field and its proposals in AI order.</summary>
    /// <param name="id">The run's id.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <exception cref="NotFoundException">No run has that id.</exception>
    public async Task<RunDetailDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        IQueryable<RunRow> query = readDb.Query<ExtractionRun>()
            .Where(run => run.Id == id)
            .Select(run => new RunRow(
                run.Id,
                run.MeetingId,
                run.MeetingNotesId,
                run.NotesSha256,
                run.StartedByUserId,
                run.Provider,
                run.Model,
                run.PromptVersion,
                run.SchemaVersion,
                run.StartedAt,
                run.DurationMs,
                run.InputTokens,
                run.OutputTokens,
                run.Outcome,
                run.FailureReason,
                run.Warnings));

        IReadOnlyList<RunRow> found = await readDb.ToListAsync(query, cancellationToken);

        if (found.Count == 0)
        {
            throw new NotFoundException("ExtractionRun", id);
        }

        RunRow row = found[0];

        return new RunDetailDto(
            row.Id,
            row.MeetingId,
            row.MeetingNotesId,
            row.NotesSha256,
            row.StartedByUserId,
            row.Provider,
            row.Model,
            row.PromptVersion,
            row.SchemaVersion,
            row.StartedAt,
            row.DurationMs,
            row.InputTokens,
            row.OutputTokens,
            row.Outcome,
            row.FailureReason,
            row.Warnings,
            await proposals.ForRunAsync(id, cancellationToken));
    }

    /// <summary>
    /// One Meeting's runs, for Meeting Detail's run list. Newest first, and totally ordered: two
    /// runs that started in the same instant order by id, so the list never reshuffles between
    /// reads.
    /// </summary>
    /// <param name="meetingId">The Meeting whose runs to list.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <exception cref="NotFoundException">No Meeting has that id.</exception>
    public async Task<IReadOnlyList<RunSummaryDto>> ListForMeetingAsync(
        Guid meetingId,
        CancellationToken cancellationToken = default)
    {
        // An empty list is an answer only for a Meeting that exists. Without this, a mistyped id
        // would read as "no runs yet" rather than as the 404 it is.
        int meetings = await readDb.CountAsync(
            readDb.Query<Meeting>().Where(meeting => meeting.Id == meetingId),
            cancellationToken);

        if (meetings == 0)
        {
            throw new NotFoundException("Meeting", meetingId);
        }

        // Hoisted out of the projection for the reason MeetingsQueries.ListAsync gives: composing
        // the correlated counts against this local is translatable, while calling
        // readDb.Query<ProposedAction>() inside the Select is a client-evaluation failure.
        IQueryable<ProposedAction> kept = readDb.Query<ProposedAction>();

        IQueryable<RunSummaryDto> query = readDb.Query<ExtractionRun>()
            .Where(run => run.MeetingId == meetingId)
            .OrderByDescending(run => run.StartedAt)
            .ThenByDescending(run => run.Id)
            .Select(run => new RunSummaryDto(
                run.Id,
                run.StartedAt,
                run.PromptVersion,
                run.Provider,
                run.Model,
                run.Outcome,
                run.FailureReason,
                kept.Count(proposal => proposal.ExtractionRunId == run.Id),
                kept.Count(proposal => proposal.ExtractionRunId == run.Id
                    && proposal.ReviewState == ReviewState.Pending)));

        return await readDb.ToListAsync(query, cancellationToken);
    }

    /// <summary>
    /// The run's own columns, projected in SQL. It exists so the projection carries only what the
    /// provider can translate: the proposals are a second query, and a collection literal inside an
    /// EF <c>Select</c> is not something to ask a translator to make sense of.
    /// </summary>
    private sealed record RunRow(
        Guid Id,
        Guid MeetingId,
        Guid MeetingNotesId,
        string NotesSha256,
        Guid StartedByUserId,
        string Provider,
        string Model,
        string PromptVersion,
        string SchemaVersion,
        DateTimeOffset StartedAt,
        int DurationMs,
        int InputTokens,
        int OutputTokens,
        ExtractionOutcome Outcome,
        string? FailureReason,
        IReadOnlyList<string> Warnings);
}
