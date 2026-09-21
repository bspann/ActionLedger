using ActionLedger.Application.Abstractions;
using ActionLedger.Domain.Users;

namespace ActionLedger.Infrastructure.Seed;

/// <summary>
/// AD-21 — the identity the seeder writes as. Seeded data is attributed to the <c>Seed</c> User,
/// so every seeded row has a real actor and the audit trail reads the same way for seeded and
/// live writes.
/// </summary>
/// <remarks>
/// This and the claims-based implementation are the only two <see cref="ICurrentUser"/>
/// implementations AD-12 permits. It is constructed from the persisted <c>Seed</c> User rather
/// than from a hard-coded id, so the attribution always points at a row that exists.
/// </remarks>
public sealed class SeedCurrentUser(User seedUser) : ICurrentUser
{
    public Guid UserId { get; } = seedUser.Id;

    public string DisplayName { get; } = seedUser.DisplayName;

    public Role Role { get; } = seedUser.Role;
}
