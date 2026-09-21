using System.Net;
using System.Net.Http.Json;
using ActionLedger.Api.Health;
using Testcontainers.PostgreSql;
using Xunit;

namespace ActionLedger.Api.Tests;

/// <summary>
/// AD-17 — <c>/health</c> is liveness and never touches the database; <c>/health/ready</c> is
/// readiness and answers 200 only when the database does. The pair is what lets compose and
/// Container Apps tell "the process is up" from "the process can serve".
/// </summary>
/// <remarks>
/// The healthy row needs a database that is really reachable, so it runs against the same
/// <c>postgres:18-alpine</c> the compose environment uses. The unhealthy row needs one that is
/// really not, so it points at a closed port — no mock stands in for either.
/// </remarks>
public sealed class ReadinessTests : IAsyncLifetime
{
    /// <summary>A port nothing listens on, with a one-second connect timeout so the probe is quick.</summary>
    private const string UnreachableDatabase =
        "Host=127.0.0.1;Port=1;Database=actionledger;Username=nobody;Password=nobody;Timeout=1";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("actionledger")
        .WithUsername("actionledger")
        .WithPassword("actionledger-tests-not-a-secret")
        .Build();

    [Fact]
    public async Task Readiness_is_200_when_the_database_answers()
    {
        await using TestApi api = new()
        {
            ConfigurationOverrides = { ["Database:ConnectionString"] = _postgres.GetConnectionString() },
        };

        using HttpClient client = api.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        HealthStatus? body = await response.Content.ReadFromJsonAsync<HealthStatus>(TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Ready, body);
    }

    [Fact]
    public async Task Readiness_is_503_when_the_database_is_unreachable()
    {
        await using TestApi api = new()
        {
            ConfigurationOverrides = { ["Database:ConnectionString"] = UnreachableDatabase },
        };

        using HttpClient client = api.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        // 503, not 200 and not a 500 from an unhandled Npgsql exception.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        HealthStatus? body = await response.Content.ReadFromJsonAsync<HealthStatus>(TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Unavailable, body);
    }

    [Fact]
    public async Task Liveness_is_200_even_when_the_database_is_unreachable()
    {
        await using TestApi api = new()
        {
            ConfigurationOverrides = { ["Database:ConnectionString"] = UnreachableDatabase },
        };

        using HttpClient client = api.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        HealthStatus? body = await response.Content.ReadFromJsonAsync<HealthStatus>(TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Healthy, body);
    }

    async ValueTask IAsyncLifetime.InitializeAsync() => await _postgres.StartAsync(TestContext.Current.CancellationToken);

    async ValueTask IAsyncDisposable.DisposeAsync() => await _postgres.DisposeAsync();
}
