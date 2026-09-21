using ActionLedger.Api.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ActionLedger.Api.Tests;

/// <summary>
/// AD-13 — every route lives under <c>/api/v1</c>; the only exceptions are <c>/health</c>,
/// <c>/openapi</c>, and <c>/swagger</c>. Asserted against the endpoints the host actually maps,
/// so a controller added later with a hand-written route cannot quietly escape the prefix.
/// </summary>
public sealed class RouteDisciplineTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Every_mapped_endpoint_is_versioned_or_on_the_short_allowlist(bool includeProbeControllers)
    {
        // Run over the host the contract is generated from, and over the same host plus the probe
        // controllers — those are real controllers going through the real convention, so they are
        // what keeps this assertion from being vacuous until Story 1.4 adds the first route.
        await using TestApi api = new() { IncludeTestEndpoints = includeProbeControllers };
        using HttpClient _ = api.CreateClient();

        string[] offenders = [.. RoutePatterns(api).Where(IsOutsideTheRules)];

        Assert.True(
            offenders.Length == 0,
            $"These routes sit outside /{ApiRoutes.Prefix} and are not on the allowlist "
            + $"({string.Join(", ", ApiRoutes.UnversionedPaths)}): {string.Join(", ", offenders)}");
    }

    [Fact]
    public async Task The_prefix_is_a_convention_so_a_controller_cannot_forget_it()
    {
        // The probe controllers declare [Route("errors")] and [Route("protected")] with no prefix
        // of their own. If the convention stopped applying, these would answer unversioned.
        await using TestApi api = new() { IncludeTestEndpoints = true };
        using HttpClient _ = api.CreateClient();

        string[] probes = [.. RoutePatterns(api).Where(route => route.Contains("errors/", StringComparison.Ordinal))];

        Assert.NotEmpty(probes);
        Assert.All(probes, route => Assert.StartsWith($"/{ApiRoutes.Prefix}/", route, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Liveness_answers_without_a_token_and_without_a_version_prefix()
    {
        await using TestApi api = new();
        using HttpClient client = api.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        Assert.Contains("healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Readiness_is_mapped_and_sits_under_the_health_allowlist()
    {
        // AD-13 lists /health/ready among the four unversioned routes. Story 1.2 asserted it was
        // absent, because there was no database for it to be honest about; this story is where it
        // becomes real. What it answers is ReadinessTests' subject — this is the route rule.
        await using TestApi api = new();
        using HttpClient client = api.CreateClient();

        Assert.Contains("/health/ready", RoutePatterns(api));
        Assert.DoesNotContain("/health/ready", RoutePatterns(api).Where(IsOutsideTheRules));
    }

    private static bool IsOutsideTheRules(string route) =>
        !route.StartsWith($"/{ApiRoutes.Prefix}/", StringComparison.Ordinal)
        && !ApiRoutes.UnversionedPaths.Any(allowed =>
            route.Equals(allowed, StringComparison.Ordinal)
            || route.StartsWith(allowed + "/", StringComparison.Ordinal));

    private static IEnumerable<string> RoutePatterns(TestApi api) =>
        api.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Select(endpoint => "/" + endpoint.RoutePattern.RawText?.TrimStart('/'))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
}
