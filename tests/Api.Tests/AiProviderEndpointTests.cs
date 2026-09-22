using System.Net;
using System.Text.Json;
using ActionLedger.Api.Errors;
using ActionLedger.Domain.Users;
using Xunit;

namespace ActionLedger.Api.Tests;

/// <summary>
/// FR-7 / AD-11 — <c>GET /api/v1/ai/provider</c>, the read Meeting Detail's in-flight caption is
/// built from. Asserted against the real host, so the values are the ones <c>AddActionLedgerAi</c>
/// actually registered rather than a stand-in.
/// </summary>
public sealed class AiProviderEndpointTests
{
    private const string Provider = "/api/v1/ai/provider";

    [Theory]
    [InlineData(nameof(Role.ActionOfficer))]
    [InlineData(nameof(Role.Lead))]
    public async Task Any_signed_in_role_reads_the_configured_provider_and_its_model(string role)
    {
        await using TestApi api = new();
        using HttpClient client = api.CreateClientAs(role);

        HttpResponseMessage response = await client.GetAsync(Provider, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonElement body = await ReadAsync(response);

        Assert.Equal("Fake", body.GetProperty("provider").GetString());
        Assert.Equal("fixture-catalog", body.GetProperty("model").GetString());

        // NFR-4 — the two values and nothing else. An endpoint, key, or base URL never crosses.
        Assert.Equal(
            ["model", "provider"],
            body.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task An_anonymous_caller_is_401()
    {
        await using TestApi api = new();
        using HttpClient client = api.CreateClient();

        HttpResponseMessage response = await client.GetAsync(Provider, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ProblemTypes.Unauthorized, (await ReadAsync(response)).GetProperty("type").GetString());
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        JsonDocument
            .Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .RootElement
            .Clone();
}
