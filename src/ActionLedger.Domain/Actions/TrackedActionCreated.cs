using ActionLedger.Domain.Common;
using ActionLedger.Domain.Extraction;

namespace ActionLedger.Domain.Actions;

/// <summary>
/// A human decision turned a proposal into tracked work. Raised on the <see cref="TrackedAction"/>
/// root by <c>ProposedAction.Decide</c>, and the event Story 3.3's outbox writes a row for.
/// </summary>
/// <remarks>
/// It carries ids and the decision kind only. The outbox's payload builder reads everything else
/// from the same context inside the commit, so the event never holds a stale copy of a row.
/// </remarks>
/// <param name="TrackedActionId">The Tracked Action the decision created.</param>
/// <param name="ProposedActionId">The proposal that was decided.</param>
/// <param name="Kind"><see cref="DecisionKind.Approved"/> or <see cref="DecisionKind.Edited"/>; a rejection creates nothing.</param>
public sealed record TrackedActionCreated(Guid TrackedActionId, Guid ProposedActionId, DecisionKind Kind) : DomainEvent;
