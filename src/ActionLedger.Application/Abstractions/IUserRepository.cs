using ActionLedger.Domain.Users;

namespace ActionLedger.Application.Abstractions;

/// <summary>
/// AD-10 — add for new roots, load-then-mutate for existing ones. There is no <c>Update</c>, no
/// <c>Attach</c>, and no graph <c>AddRange</c>: a loaded root is already tracked, so changing it
/// is enough. Saving is <see cref="IUnitOfWork"/>'s job, never a repository's.
/// </summary>
public interface IUserRepository
{
    /// <summary>Stages a new User for the next commit.</summary>
    void Add(User user);

    /// <summary>Loads a User by id, or <c>null</c>.</summary>
    Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a User by sign-in name, or <c>null</c>. The name is matched as stored — lower-cased
    /// and trimmed by <see cref="User.Register"/> — so callers pass what the user typed.
    /// </summary>
    Task<User?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default);
}
