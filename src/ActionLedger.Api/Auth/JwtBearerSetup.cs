using System.Text;
using ActionLedger.Api.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ActionLedger.Api.Auth;

/// <summary>
/// AD-12 — the bearer scheme the Api accepts. This story wires acceptance and the authn/authz
/// middleware; Story 1.4 adds the endpoint that issues a token against the same key and issuer.
/// </summary>
public static class JwtBearerSetup
{
    /// <summary>The claim carrying the acting User's id. Handlers resolve the actor from it, never from a body.</summary>
    public const string SubjectClaim = "sub";

    /// <summary>The claim carrying the acting User's display name.</summary>
    public const string NameClaim = "name";

    /// <summary>The claim carrying the acting User's role.</summary>
    public const string RoleClaim = "role";

    /// <summary>Registers HS256 bearer authentication and authorization.</summary>
    public static IServiceCollection AddApiAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureJwtBearerOptions>();
        services.AddAuthorization();

        return services;
    }
}

/// <summary>
/// Configures the bearer handler from the validated <see cref="JwtOptions"/> rather than from raw
/// configuration, so the AD-16 startup validation is what guards the signing key.
/// </summary>
internal sealed class ConfigureJwtBearerOptions(IOptions<JwtOptions> jwt)
    : IConfigureNamedOptions<JwtBearerOptions>
{
    public void Configure(string? name, JwtBearerOptions options)
    {
        if (!string.Equals(name, JwtBearerDefaults.AuthenticationScheme, StringComparison.Ordinal))
        {
            return;
        }

        JwtOptions settings = jwt.Value;

        // Keep the wire claim names. The .NET default remaps `sub` to a SOAP-era URI, which would
        // break AD-12's "the actor is the `sub` claim" the moment a handler reads it.
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = settings.Issuer,

            // No audience key exists in the spine's Config keys row: one issuer, one audience.
            ValidateAudience = false,

            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Key)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],

            ClockSkew = TimeSpan.FromSeconds(30),

            NameClaimType = JwtBearerSetup.NameClaim,
            RoleClaimType = JwtBearerSetup.RoleClaim,
        };
    }

    public void Configure(JwtBearerOptions options) => Configure(Options.DefaultName, options);
}
