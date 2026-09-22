using ActionLedger.Application.Abstractions;
using ActionLedger.Domain.Actions;
using ActionLedger.Domain.Extraction;
using ActionLedger.Domain.Meetings;

namespace ActionLedger.Application.Meetings;

/// <summary>
/// AD-2 — reads are per-feature query classes over <see cref="IReadDb"/>, never handlers and never
/// repositories. The shape is <c>UsersQueries</c>'; what differs is the order and the projection.
/// </summary>
public sealed class MeetingsQueries(IReadDb readDb)
{
    /// <summary>
    /// The Meeting List, paged. Newest meeting first, and totally ordered: two meetings on the same
    /// day order by when they were created, and the id breaks the remaining tie, so no two rows can
    /// tie and paging over an unchanging set cannot return a row twice or skip one. The count and
    /// the window below are separate statements with no snapshot across them, so a meeting inserted
    /// between the two can still shift a row across a page boundary.
    /// </summary>
    /// <param name="page">The 1-based page, clamped by <see cref="Paging.Normalize"/>.</param>
    /// <param name="pageSize">Items per page, clamped by <see cref="Paging.Normalize"/>.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    public async Task<PagedResult<MeetingSummaryDto>> ListAsync(
        int? page,
        int? pageSize,
        CancellationToken cancellationToken = default)
    {
        (int normalizedPage, int normalizedPageSize) = Paging.Normalize(page, pageSize);

        IQueryable<Meeting> meetings = readDb.Query<Meeting>();

        // Hoisted out of the projection on purpose. Composing the correlated counts below against
        // these locals is translatable; calling readDb.Query<T>() *inside* the Select would put a
        // method call on a captured object into the expression tree, which EF cannot translate
        // and would answer with a client-evaluation failure at runtime.
        IQueryable<ExtractionRun> runs = readDb.Query<ExtractionRun>();
        IQueryable<ProposedAction> proposals = readDb.Query<ProposedAction>();
        IQueryable<TrackedAction> trackedActions = readDb.Query<TrackedAction>();

        // Counted before the window is applied, so `total` is every matching row rather than the
        // length of this page (Consistency Conventions, Paging row).
        int total = await readDb.CountAsync(meetings, cancellationToken);

        // Paging.Normalize puts a ceiling on pageSize but not on page, so the offset is computed
        // wide and compared before it is narrowed: a huge `page` would otherwise overflow int and
        // send PostgreSQL a negative OFFSET.
        long offset = (long)(normalizedPage - Paging.FirstPage) * normalizedPageSize;

        if (offset >= total)
        {
            // Past the end is an empty page that still reports the true total, not an error.
            return new PagedResult<MeetingSummaryDto>([], normalizedPage, normalizedPageSize, total);
        }

        IQueryable<MeetingSummaryDto> window = meetings
            .OrderByDescending(meeting => meeting.MeetingDate)
            .ThenByDescending(meeting => meeting.CreatedAt)
            .ThenByDescending(meeting => meeting.Id)
            // Narrowing is safe: offset < total, and total is an int.
            .Skip((int)offset)
            .Take(normalizedPageSize)
            // Both counts are correlated subqueries rather than joins, so a Meeting with no runs
            // still appears, with 0 for each. A Tracked Action reaches its Meeting only through
            // the proposal it was decided from and that proposal's run, so its count walks that
            // chain rather than any column of its own.
            .Select(meeting => new MeetingSummaryDto(
                meeting.Id,
                meeting.Title,
                meeting.MeetingDate,
                runs.Count(run => run.MeetingId == meeting.Id),
                trackedActions.Count(action => proposals.Any(proposal =>
                    proposal.Id == action.ProposedActionId
                    && runs.Any(run => run.Id == proposal.ExtractionRunId && run.MeetingId == meeting.Id)))));

        IReadOnlyList<MeetingSummaryDto> items = await readDb.ToListAsync(window, cancellationToken);

        return new PagedResult<MeetingSummaryDto>(items, normalizedPage, normalizedPageSize, total);
    }

    /// <summary>One Meeting, with its notes as they were pasted.</summary>
    /// <param name="id">The Meeting's id.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <exception cref="NotFoundException">No Meeting has that id.</exception>
    public async Task<MeetingDetailDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        IQueryable<MeetingDetailDto> query = readDb.Query<Meeting>()
            .Where(meeting => meeting.Id == id)
            .Select(meeting => new MeetingDetailDto(
                meeting.Id,
                meeting.Title,
                meeting.MeetingDate,
                meeting.Attendees,
                meeting.CreatedByUserId,
                meeting.CreatedAt,
                meeting.Notes == null
                    ? null
                    : new MeetingNotesDto(
                        meeting.Notes.Id,
                        meeting.Notes.Text,
                        meeting.Notes.Sha256,
                        meeting.Notes.SavedAt)));

        IReadOnlyList<MeetingDetailDto> found = await readDb.ToListAsync(query, cancellationToken);

        return found.Count == 0
            ? throw new NotFoundException("Meeting", id)
            : found[0];
    }
}
