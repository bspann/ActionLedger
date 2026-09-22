using ActionLedger.Domain.Meetings;

namespace ActionLedger.Application.Abstractions;

/// <summary>
/// AD-10 — add for new roots, load-then-mutate for existing ones. There is no <c>Update</c>, no
/// <c>Attach</c>, and no save: a loaded root is already tracked, so changing it is the whole of
/// the write, and committing is <see cref="IUnitOfWork"/>'s job.
/// </summary>
public interface IMeetingRepository
{
    /// <summary>Stages a new Meeting for the next commit.</summary>
    void Add(Meeting meeting);

    /// <summary>
    /// Loads a Meeting by id, or <c>null</c>. The Meeting's notes come with it, so
    /// <see cref="Meeting.HasNotes"/> is answered from what is actually stored rather than from a
    /// navigation nobody loaded — which is what makes the write-once rule real for a second save.
    /// </summary>
    Task<Meeting?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);
}
