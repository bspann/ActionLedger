using System.Diagnostics;
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

    /// <summary>
    /// Keeps an <see cref="Activity"/> alive for the request under test.
    /// </summary>
    /// <remarks>
    /// ASP.NET Core creates the request activity only while something listens to its
    /// <see cref="ActivitySource"/>, and that registration is process-global: every host adds it
    /// on start and removes it on dispose. With test classes running in parallel, one host's
    /// teardown can remove the listener while another's request is in flight — the request then
    /// has no activity, <c>Activity.Current</c> is null, and the correlation id falls back to
    /// <c>HttpContext.TraceIdentifier</c>. That is correct product behaviour and the wrong
    /// precondition for this assertion, so the test establishes it rather than inheriting it.
    /// </remarks>
    private static ActivityListener ListenToAspNetCore()
    {
        ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };

        ActivitySource.AddActivityListener(listener);

        return listener;
    }

    [Fact]
    public async Task An_incoming_traceparent_becomes_the_correlation_id()
    {
        using ActivityListener listener = ListenToAspNetCore();

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
        using ActivityListener listener = ListenToAspNetCore();

        await using TestApi api = new() { IncludeTestEndpoints = true };
        using HttpClient client = api.CreateClient();

        JsonElement first = await client.GetFromJsonAsync<JsonElement>("/api/v1/errors/correlation", TestContext.Current.CancellationToken);
        JsonElement second = await client.GetFromJsonAsync<JsonElement>("/api/v1/errors/correlation", TestContext.Current.CancellationToken);

        Assert.NotEqual(
            first.GetProperty("correlationId").GetString(),
            second.GetProperty("correlationId").GetString());
    }
}
