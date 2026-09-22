using ActionLedger.Domain.Actions;

namespace ActionLedger.Application.Abstractions;

/// <summary>
/// AD-7 — append and ordered read only. There is no update member and no delete member on this
/// port, and there never will be: an audit row that can be rewritten is not an audit row.
/// </summary>
/// <remarks>
/// Revisions are minted inside aggregate methods, never here. This port only carries what an
/// aggregate already produced into the same commit the aggregate's own change is part of (AD-20).
/// </remarks>
public interface IActionRevisionRepository
{
    /// <summary>Stages revisions an aggregate method returned for the next commit.</summary>
    void AddRange(IReadOnlyList<ActionRevision> revisions);

    /// <summary>
    /// The Audit Trail's one ordered read: every revision for a target, by <c>OccurredAt</c> then
    /// <c>Sequence</c> (AD-7). The two-key order matters because one aggregate call stamps every
    /// revision it produced with the same instant, so the sequence is what breaks that tie.
    /// </summary>
    Task<IReadOnlyList<ActionRevision>> ListForTargetAsync(
        RevisionTargetType targetType,
        Guid targetId,
        CancellationToken cancellationToken = default);
}
