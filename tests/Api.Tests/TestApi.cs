using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ActionLedger.Api.Auth;
using ActionLedger.Api.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace ActionLedger.Api.Tests;

/// <summary>
/// The host under test. AD-16 makes every option group validate at startup, so the factory has
/// to supply a complete, valid configuration before <c>WebApplicationFactory</c> can boot —
/// which is itself part of what this story asserts.
/// </summary>
public class TestApi : WebApplicationFactory<Program>
{
    /// <summary>A key long enough for HS256, and obviously not a credential.</summary>
    public const string SigningKey = "action-ledger-test-signing-key-not-a-secret";

    public const string Issuer = "actionledger-tests";

    /// <summary>The configuration a valid host needs. Tests override or remove single keys from it.</summary>
    public static Dictionary<string, string?> ValidConfiguration() => new(StringComparer.Ordinal)
    {
        ["Ai:Provider"] = "Fake",
        ["Ai:PromptVersion"] = "v1",
        ["Ai:CallTimeoutSeconds"] = "90",
        ["Ai:LowConfidenceThreshold"] = "0.70",
        ["Jwt:Key"] = SigningKey,
        ["Jwt:Issuer"] = Issuer,
        ["Database:ConnectionString"] = "Host=localhost;Database=actionledger;Username=test;Password=test",
        ["Webhooks:TimeoutSeconds"] = "10",
        ["Webhooks:LeaseSeconds"] = "60",
        ["Seed:Enabled"] = "false",
    };

    /// <summary>Overrides applied on top of <see cref="ValidConfiguration"/>. A null value removes the key.</summary>
    public Dictionary<string, string?> ConfigurationOverrides { get; init; } = new(StringComparer.Ordinal);

    /// <summary>The environment the host runs as. Drives whether Swagger UI is mapped.</summary>
    public string Environment { get; init; } = Environments.Development;

    /// <summary>
    /// True when the test-only controllers in this assembly should be part of the application.
    /// Off by default so the contract the snapshot guards never contains a test route.
    /// </summary>
    public bool IncludeTestEndpoints { get; init; }

    /// <summary>
    /// Mints a token the host will accept: same key, same issuer, wire claim names per AD-12.
    /// Story 1.4 adds the endpoint that issues these; the scheme that accepts them is this story's.
    /// </summary>
    public static string TokenFor(string role, string? subject = null, string displayName = "Test User")
    {
        SigningCredentials credentials = new(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
            SecurityAlgorithms.HmacSha256);

        JwtSecurityToken token = new(
            issuer: Issuer,
            audience: null,
            claims:
            [
                new Claim(JwtBearerSetup.SubjectClaim, subject ?? Guid.CreateVersion7().ToString()),
                new Claim(JwtBearerSetup.NameClaim, displayName),
                new Claim(JwtBearerSetup.RoleClaim, role),
            ],
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.Add(JwtOptions.TokenLifetime),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>An <see cref="HttpClient"/> that sends a bearer token on every request.</summary>
    public HttpClient CreateClientAs(string role)
    {
        HttpClient client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TokenFor(role));

        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environment);

        builder.ConfigureAppConfiguration(configuration =>
        {
            Dictionary<string, string?> settings = ValidConfiguration();

            foreach ((string key, string? value) in ConfigurationOverrides)
            {
                if (value is null)
                {
                    settings.Remove(key);
                }
                else
                {
                    settings[key] = value;
                }
            }

            // Last provider wins, but appsettings.json still supplies a value for any key a test
            // means to remove. Clear the file providers so "remove the key" actually removes it.
            configuration.Sources.Clear();
            configuration.AddInMemoryCollection(settings);
        });

        if (IncludeTestEndpoints)
        {
            builder.ConfigureTestServices(services =>
                services.AddControllers().AddApplicationPart(typeof(TestApi).Assembly));
        }
    }
}
