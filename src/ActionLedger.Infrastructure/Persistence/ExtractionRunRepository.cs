using ActionLedger.Application.Abstractions;
using ActionLedger.Domain.Extraction;
using Microsoft.EntityFrameworkCore;

namespace ActionLedger.Infrastructure.Persistence;

/// <summary>
/// AD-10 — add for new roots, load for existing ones, and never save. There is no <c>Update</c>
/// and no <c>Attach</c>: a run is inserted once, with its proposals, and never revised.
/// </summary>
/// <remarks>
/// The proposals need an <c>Include</c>. They are a <c>HasMany</c> rather than an owned collection
/// (see <c>Configurations.ExtractionRunConfiguration</c> for why), so EF does not load them with
/// the root — and an aggregate whose list is empty because nobody asked for it would be a trap for
/// the first caller that trusts it.
/// </remarks>
internal sealed class ExtractionRunRepository(AppDbContext context) : IExtractionRunRepository
{
    public void Add(ExtractionRun run) => context.ExtractionRuns.Add(run);

    public Task<ExtractionRun?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.ExtractionRuns
            // Ordered, because `ExtractionRun.Proposals` promises the order the provider returned
            // them and a bare Include materializes in whatever order the planner chose. The read
            // side asks for it too (`ProposedActionReadModel`); this is the same rule on the load
            // side, so the first caller to trust the aggregate's list gets what it documents.
            .Include(run => run.Proposals.OrderBy(proposal => proposal.Ordinal))
            .FirstOrDefaultAsync(run => run.Id == id, cancellationToken);
}
