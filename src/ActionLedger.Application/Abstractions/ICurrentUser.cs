using ActionLedger.Domain.Users;

namespace ActionLedger.Application.Abstractions;

/// <summary>
/// AD-12 — who is making this write. Resolved from the token's <c>sub</c> claim, never from a
/// request body or a query parameter. A handler that needs an actor takes it from here.
/// </summary>
/// <remarks>
/// There are exactly two implementations: the one that reads the claims principal, and
/// <c>SeedCurrentUser</c>, which the seeder scopes to the <c>Seed</c> User (AD-21). No other code
/// may substitute this port.
/// </remarks>
public interface ICurrentUser
{
    /// <summary>The acting User's id.</summary>
    Guid UserId { get; }

    /// <summary>The acting User's display name, as it appears in the audit trail.</summary>
    string DisplayName { get; }

    /// <summary>The acting User's role.</summary>
    Role Role { get; }
}
