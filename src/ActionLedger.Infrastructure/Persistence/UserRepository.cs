using ActionLedger.Application.Abstractions;
using ActionLedger.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace ActionLedger.Infrastructure.Persistence;

/// <summary>
/// AD-10 — add for new roots, load-then-mutate for existing ones, and never save. There is no
/// <c>Update</c> and no <c>Attach</c> here by design: a loaded root is tracked, so mutating it is
/// the whole of the write, and <c>Infrastructure.Tests</c> asserts a new aggregate persists as
/// inserts only.
/// </summary>
internal sealed class UserRepository(AppDbContext context) : IUserRepository
{
    public void Add(User user) => context.Users.Add(user);

    public Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Users.FirstOrDefaultAsync(user => user.Id == id, cancellationToken);

    public Task<User?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        string normalized = (username ?? string.Empty).Trim().ToLowerInvariant();

        return context.Users.FirstOrDefaultAsync(user => user.Username == normalized, cancellationToken);
    }
}
