using System.Security.Claims;
using ActionLedger.Application.Abstractions;
using ActionLedger.Domain.Users;

namespace ActionLedger.Api.Auth;

/// <summary>
/// AD-12, FR-25 — the acting User, read from the token's claims. This and <c>SeedCurrentUser</c>
/// are the only two <see cref="ICurrentUser"/> implementations the architecture permits.
/// </summary>
/// <remarks>
/// <para>
/// The claims are snapshotted at construction, matching <c>SeedCurrentUser</c>'s shape: a handler
/// that reads the actor twice in one unit of work cannot see it change underneath it.
/// </para>
/// <para>
/// It throws when there is no authenticated principal, and when a claim it needs is missing or
/// malformed. Resolving an actor is not a best-effort operation: a write attributed to a guessed
/// identity is worse than a failed request, so there is no fallback and no default.
/// </para>
/// </remarks>
internal sealed class ClaimsPrincipalCurrentUser : ICurrentUser
{
    public ClaimsPrincipalCurrentUser(IHttpContextAccessor accessor)
    {
        ClaimsPrincipal? principal = accessor.HttpContext?.User;

        if (principal?.Identity?.IsAuthenticated is not true)
        {
            throw new InvalidOperationException(
                "There is no authenticated user on this request. ICurrentUser is resolvable only "
                + "behind [Authorize]; it never invents an actor.");
        }

        string subject = Require(principal, JwtBearerSetup.SubjectClaim);

        UserId = Guid.TryParse(subject, out Guid id)
            ? id
            : throw Malformed(JwtBearerSetup.SubjectClaim);

        DisplayName = Require(principal, JwtBearerSetup.NameClaim);

        // IsDefined as well as TryParse: TryParse happily returns true for "99" and for any other
        // number, which would make (Role)99 an authenticated actor holding a role that does not
        // exist. Every token this Api issues carries Enum.ToString(), so a name is all there is.
        Role = Enum.TryParse(Require(principal, JwtBearerSetup.RoleClaim), out Role role)
            && Enum.IsDefined(role)
            ? role
            : throw Malformed(JwtBearerSetup.RoleClaim);
    }

    public Guid UserId { get; }

    public string DisplayName { get; }

    public Role Role { get; }

    private static string Require(ClaimsPrincipal principal, string claim) =>
        principal.FindFirst(claim)?.Value is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException(
                $"The bearer token carries no '{claim}' claim. Every token this Api issues does.");

    private static InvalidOperationException Malformed(string claim) =>
        new($"The bearer token's '{claim}' claim is not a value this Api issued.");
}
