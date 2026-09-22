using ActionLedger.Domain.Actions;

namespace ActionLedger.Application.Abstractions;

/// <summary>
/// AD-10 — add for new Tracked Actions, and nothing else yet. There is no <c>Update</c>, no
/// <c>Attach</c>, and no save: committing is <see cref="IUnitOfWork"/>'s job.
/// </summary>
/// <remarks>
/// Only <c>ProposedAction.Decide</c> can mint a <see cref="TrackedAction"/>, so this port only ever
/// carries one a decision already produced into the same commit as the decision itself (AD-3,
/// AD-20). Epic 4 adds the load its status transitions and edits need.
/// </remarks>
public interface IActionRepository
{
    /// <summary>Stages a Tracked Action a decision returned for the next commit.</summary>
    void Add(TrackedAction trackedAction);
}
