using ActionLedger.Application.Abstractions;
using ActionLedger.Domain.Meetings;
using Microsoft.EntityFrameworkCore;

namespace ActionLedger.Infrastructure.Persistence;

/// <summary>
/// AD-10 — add for new roots, load-then-mutate for existing ones, and never save. There is no
/// <c>Update</c> and no <c>Attach</c> here by design: a loaded root is tracked, so mutating it is
/// the whole of the write.
/// </summary>
/// <remarks>
/// The notes need no <c>Include</c>. They are an owned type (see
/// <c>Configurations.MeetingConfiguration</c>), so EF Core loads them with the Meeting — which is
/// what makes <see cref="Meeting.HasNotes"/> answer from the database rather than from a
/// navigation nobody asked for, and therefore what makes the write-once rule hold on a second save.
/// </remarks>
internal sealed class MeetingRepository(AppDbContext context) : IMeetingRepository
{
    public void Add(Meeting meeting) => context.Meetings.Add(meeting);

    public Task<Meeting?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Meetings.FirstOrDefaultAsync(meeting => meeting.Id == id, cancellationToken);
}
