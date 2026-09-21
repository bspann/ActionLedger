using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace ActionLedger.Api.Tests;

/// <summary>
/// Consistency Conventions, Logging and correlation row — inside a request the correlation id is
/// the W3C trace id when a trace context is in flight, otherwise the request trace identifier.
/// </summary>
public sealed class CorrelationIdTests
{
    private const string IncomingTraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

    [Fact]
    public async Task An_incoming_traceparent_becomes_the_correlation_id()
    {
        await using TestApi api = new() { IncludeTestEndpoints = true };
        using HttpClient client = api.CreateClient();

        using HttpRequestMessage request = new(HttpMethod.Get, "/api/v1/errors/correlation");
        request.Headers.Add("traceparent", $"00-{IncomingTraceId}-00f067aa0ba902b7-01");

        HttpResponseMessage response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal(IncomingTraceId, body.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task A_request_without_a_trace_context_still_carries_a_correlation_id()
    {
        await using TestApi api = new() { IncludeTestEndpoints = true };
        using HttpClient client = api.CreateClient();

        JsonElement body = await client.GetFromJsonAsync<JsonElement>(
            "/api/v1/errors/correlation",
            TestContext.Current.CancellationToken);

        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("correlationId").GetString()));
    }

    [Fact]
    public async Task Two_requests_do_not_share_a_correlation_id()
    {
        await using TestApi api = new() { IncludeTestEndpoints = true };
        using HttpClient client = api.CreateClient();

        JsonElement first = await client.GetFromJsonAsync<JsonElement>("/api/v1/errors/correlation", TestContext.Current.CancellationToken);
        JsonElement second = await client.GetFromJsonAsync<JsonElement>("/api/v1/errors/correlation", TestContext.Current.CancellationToken);

        Assert.NotEqual(
            first.GetProperty("correlationId").GetString(),
            second.GetProperty("correlationId").GetString());
    }
}
