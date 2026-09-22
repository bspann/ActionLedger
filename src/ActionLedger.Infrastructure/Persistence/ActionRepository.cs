using ActionLedger.Application.Abstractions;
using ActionLedger.Domain.Actions;

namespace ActionLedger.Infrastructure.Persistence;

/// <summary>
/// AD-10 — add for new Tracked Actions, and never save. There is no <c>Update</c> and no
/// <c>Attach</c>; committing is <see cref="IUnitOfWork"/>'s job.
/// </summary>
/// <remarks>
/// The Tracked Action is added in the same unit of work as the decision that minted it, so the
/// <c>tracked_actions(proposed_action_id)</c> unique index and the proposal's <c>xmin</c> token
/// refuse a second decision in the same commit that would have written it (AD-20).
/// </remarks>
internal sealed class ActionRepository(AppDbContext context) : IActionRepository
{
    public void Add(TrackedAction trackedAction) => context.TrackedActions.Add(trackedAction);
}
