using ActionLedger.Application.Abstractions;
using ActionLedger.Domain.Actions;
using Microsoft.EntityFrameworkCore;

namespace ActionLedger.Infrastructure.Persistence;

/// <summary>
/// AD-7 — append and ordered read only. There is deliberately no update and no delete here: the
/// port does not expose either, and an audit row that can be rewritten is not an audit row.
/// </summary>
/// <remarks>
/// <c>AddRange</c> stages rows an aggregate method already minted, so they join the same commit as
/// the aggregate's own change (AD-20). It is not the graph <c>AddRange</c> AD-10 rules out — these
/// are flat roots with no navigation to anything.
/// </remarks>
internal sealed class ActionRevisionRepository(AppDbContext context) : IActionRevisionRepository
{
    public void AddRange(IReadOnlyList<ActionRevision> revisions) => context.ActionRevisions.AddRange(revisions);

    public async Task<IReadOnlyList<ActionRevision>> ListForTargetAsync(
        RevisionTargetType targetType,
        Guid targetId,
        CancellationToken cancellationToken = default) =>
        await context.ActionRevisions
            // A read, so it never becomes a write by someone mutating what came back.
            .AsNoTracking()
            .Where(revision => revision.TargetType == targetType && revision.TargetId == targetId)
            // AD-7's order. The instant comes first because it is what a reader sees; the sequence
            // breaks the tie one aggregate call creates by stamping every row with one instant.
            .OrderBy(revision => revision.OccurredAt)
            .ThenBy(revision => revision.Sequence)
            .ToListAsync(cancellationToken);
}
