using ActionLedger.Domain.Extraction;

namespace ActionLedger.Application.Abstractions;

/// <summary>
/// AD-3 / AD-10 — add for new roots, load for existing ones, and never save. Committing is
/// <see cref="IUnitOfWork"/>'s job, and there is no <c>Update</c> and no <c>Attach</c>: a run is
/// written once and never revised.
/// </summary>
public interface IExtractionRunRepository
{
    /// <summary>Stages a new run — and, through it, its proposals — for the next commit.</summary>
    void Add(ExtractionRun run);

    /// <summary>
    /// Loads a run by id, or <c>null</c>. The run's proposals come with it, so a caller never sees
    /// an aggregate whose list is empty because nobody asked for it.
    /// </summary>
    Task<ExtractionRun?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);
}
