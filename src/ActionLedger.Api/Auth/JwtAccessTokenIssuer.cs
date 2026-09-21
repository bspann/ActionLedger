using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ActionLedger.Api.Configuration;
using ActionLedger.Application.Abstractions;
using ActionLedger.Domain.Users;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ActionLedger.Api.Auth;

/// <summary>
/// AD-12 — issues the token <see cref="JwtBearerSetup"/> validates: HS256 over <c>Jwt:Key</c>,
/// <c>Jwt:Issuer</c> as the issuer, an 8-hour lifetime, and the claims <c>sub</c>, <c>name</c>,
/// and <c>role</c>.
/// </summary>
/// <remarks>
/// <para>
/// It lives in the Api ring because <see cref="JwtOptions"/> does, and AD-1 Rule 4 forbids
/// Infrastructure referencing Api. That is also why <see cref="IAccessTokenIssuer"/> exists:
/// Application asks for a token without ever naming a <c>Microsoft.IdentityModel</c> type.
/// </para>
/// <para>
/// The claims are attached to a <see cref="JwtSecurityToken"/> that is built and then written,
/// rather than described through a <c>SecurityTokenDescriptor</c>. That matters: the descriptor
/// path runs the outbound claim map, which would rewrite <c>sub</c>, <c>name</c>, and
/// <c>role</c> into SOAP-era URIs — and the scheme sets <c>MapInboundClaims = false</c>, so
/// nothing would map them back. This is the same construction <c>TestApi.TokenFor</c> uses.
/// </para>
/// <para>
/// There is no <c>aud</c> claim, because <c>ValidateAudience</c> is deliberately false and the
/// spine's config keys have no <c>Jwt:Audience</c>.
/// </para>
/// </remarks>
internal sealed class JwtAccessTokenIssuer(IOptions<JwtOptions> jwt, IClock clock) : IAccessTokenIssuer
{
    public AccessToken Issue(User user)
    {
        JwtOptions settings = jwt.Value;

        // AD-15 — the lifetime is measured from the clock port, never DateTimeOffset.UtcNow.
        DateTimeOffset issuedAt = clock.UtcNow;
        DateTimeOffset expiresAt = issuedAt.Add(JwtOptions.TokenLifetime);

        SigningCredentials credentials = new(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Key)),
            SecurityAlgorithms.HmacSha256);

        JwtSecurityToken token = new(
            issuer: settings.Issuer,
            audience: null,
            claims:
            [
                new Claim(JwtBearerSetup.SubjectClaim, user.Id.ToString()),
                new Claim(JwtBearerSetup.NameClaim, user.DisplayName),
                new Claim(JwtBearerSetup.RoleClaim, user.Role.ToString()),
            ],
            notBefore: issuedAt.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
