using ActionLedger.Application.Abstractions;
using ActionLedger.Application.Review;
using ActionLedger.Domain.Extraction;

namespace ActionLedger.Application.Extraction;

/// <summary>
/// AD-2 — reads are per-feature query classes over <see cref="IReadDb"/>. This one serves
/// <c>GET /api/v1/runs/{id}</c>, which FR-9 renders as Run Detail.
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
