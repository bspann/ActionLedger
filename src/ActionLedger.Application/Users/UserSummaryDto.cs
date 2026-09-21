using ActionLedger.Domain.Users;

namespace ActionLedger.Application.Users;

/// <summary>
/// AD-13 — the only shape of a User that crosses the boundary. The roster, the sign-in response,
/// and the web app's owner picker all read this and nothing wider.
/// </summary>
/// <remarks>
/// There is deliberately no <c>username</c>, no <c>passwordHash</c>, and no <c>isSystem</c>:
/// attribution is by display name, sign-in is by id, and a system identity never appears in a
/// list the web app renders.
/// </remarks>
/// <param name="Id">The User's id — the value the token's <c>sub</c> claim carries.</param>
/// <param name="DisplayName">The name shown in the roster, the toolbar, and the audit trail.</param>
/// <param name="Role">The User's role.</param>
public sealed record UserSummaryDto(Guid Id, string DisplayName, Role Role)
{
    /// <summary>Projects a User onto the boundary shape.</summary>
    public static UserSummaryDto From(User user) => new(user.Id, user.DisplayName, user.Role);
}
