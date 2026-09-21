using ActionLedger.Domain.Users;

namespace ActionLedger.Application.Abstractions;

/// <summary>
/// AD-12 — mints the bearer token a signed-in User carries. The implementation lives in the Api
/// ring, beside the <c>Jwt</c> options and the scheme that validates what it issues.
/// </summary>
/// <remarks>
/// The port returns a string and an instant rather than a token object, because every type that
/// could describe a JWT lives under <c>Microsoft.IdentityModel.*</c> and AD-1 keeps this ring
/// clear of it.
/// </remarks>
public interface IAccessTokenIssuer
{
    /// <summary>Issues a token for <paramref name="user"/>, valid from now.</summary>
    AccessToken Issue(User user);
}

/// <summary>A signed token and the instant it stops being valid.</summary>
/// <param name="Token">The encoded token, sent as <c>Authorization: Bearer {token}</c>.</param>
/// <param name="ExpiresAt">When the token expires, in UTC.</param>
public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);
