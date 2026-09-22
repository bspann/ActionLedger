using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using ActionLedger.Application.Abstractions;
using ActionLedger.Application.Ai;
using ActionLedger.Infrastructure.Ai;
using ActionLedger.Infrastructure.Ai.Providers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ActionLedger.Api.Tests;

/// <summary>
/// AD-11, AD-16 — the composition root's <c>AddActionLedgerAi</c> call, asserted against a real
/// host rather than against a hand-assembled container.
/// </summary>
/// <remarks>
/// <c>AiStartupCheckTests</c> in <c>Infrastructure.Tests</c> builds its own
/// <c>ServiceCollection</c>, and every existing <c>StartupValidationTests</c> failure comes from
/// <c>AiOptions</c> — so deleting the whole registration block from <c>Program.cs</c> once left the
/// entire suite green. These tests are what notices.
/// </remarks>
public sealed class AiRegistrationTests
{
    /// <summary>
    /// The seam the rest of Epic 2 resolves out of the real host: one extractor, and a provider-info
    /// port reporting what <c>Ai:Provider</c> actually selected.
    /// </summary>
    [Fact]
    public void The_host_wires_the_ai_seam()
    {
        using TestApi api = new();

        using IServiceScope scope = api.Services.CreateScope();

        Assert.IsType<ChatClientActionExtractor>(scope.ServiceProvider.GetRequiredService<IActionExtractor>());

        IAiProviderInfo provider = scope.ServiceProvider.GetRequiredService<IAiProviderInfo>();

        Assert.Equal("Fake", provider.Provider);
        Assert.Equal("fixture-catalog", provider.Model);
    }

    /// <summary>
    /// AD-6, AD-16 — a configured prompt version with no embedded file is a refusal to start, and
    /// the message names the version. <c>AiOptions</c> cannot catch this: <c>v99</c> is a perfectly
    /// well-formed value, and whether a file for it exists is a fact about the Infrastructure
    /// assembly's resources.
    /// </summary>
    [Fact]
    public async Task A_prompt_version_with_no_embedded_file_stops_the_host_before_it_serves()
    {
        await using TestApi api = new() { ConfigurationOverrides = { ["Ai:PromptVersion"] = "v99" } };

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Task.Run(() => api.CreateClient(), TestContext.Current.CancellationToken));

        Assert.Contains("v99", failure.Message, StringComparison.Ordinal);
        Assert.Contains("v1", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The projection in <c>Program.cs</c> that turns validated <c>AiOptions</c> into
    /// <c>AiSettings</c> carries the configured values, not defaults.
    /// </summary>
    /// <remarks>
    /// Without this, replacing the accessor body with a hard-wired
    /// <c>new AiSettings("Fake", ai.PromptVersion, 90)</c> left the whole suite green:
    /// <c>Ai:Provider</c> is only ever configured as <c>Fake</c> elsewhere, and
    /// <c>Ai:CallTimeoutSeconds</c> — the per-call budget NFR-1's ceiling is computed from —
    /// reached no assertion at all outside a hand-built <c>AiSettings</c>.
    /// </remarks>
    [Fact]
    public void The_composition_root_carries_the_configured_ai_values_into_the_ring()
    {
        using TestApi api = new() { ConfigurationOverrides = { ["Ai:CallTimeoutSeconds"] = "7" } };

        AiSettings settings = api.Services.GetRequiredService<AiSettings>();

        Assert.Equal("Fake", settings.Provider);
        Assert.Equal("v1", settings.PromptVersion);
        Assert.Equal(7, settings.CallTimeoutSeconds);
    }

    /// <summary>
    /// FR-7 at the host: <c>Ai:Provider=AzureOpenAI</c> with a complete sub-section starts with no
    /// network at all (the probe is credential presence), and the provider-info port reports the
    /// configured provider and the deployment name the factory will call.
    /// </summary>
    [Fact]
    public async Task A_complete_azure_section_starts_the_host_and_reports_its_model()
    {
        await using TestApi api = new()
        {
            ConfigurationOverrides =
            {
                ["Ai:Provider"] = "AzureOpenAI",
                ["Ai:AzureOpenAI:Endpoint"] = "https://example-resource.openai.azure.com/openai/v1/",
                ["Ai:AzureOpenAI:Model"] = "gpt-4o-mini",
                ["Ai:AzureOpenAI:ApiKey"] = "not-a-real-key",
            },
        };

        using HttpClient client = api.CreateClient();

        (await client.GetAsync("/health", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

        IAiProviderInfo provider = api.Services.GetRequiredService<IAiProviderInfo>();

        Assert.Equal("AzureOpenAI", provider.Provider);
        Assert.Equal("gpt-4o-mini", provider.Model);
    }

    /// <summary>
    /// FR-7 through the real composition root: <c>Ai:Provider=LocalOpenAI</c> against a loopback
    /// OpenAI-compatible stub starts the host (the probe finds the model) and a run through the
    /// host's own <see cref="IActionExtractor"/> succeeds with the fixture's proposals.
    /// </summary>
    [Fact]
    public async Task A_local_provider_run_through_the_host_succeeds()
    {
        const string Model = "stub-model";

        FixtureCase fixture = new FixtureCatalog().Cases.Single(candidate => candidate.Stem == "office-move-planning");

        using HttpListener listener = new();
        int port = ClosedPort();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        Task serving = ServeAsync(listener, Model, fixture.ExpectedJson);

        await using TestApi api = new()
        {
            ConfigurationOverrides =
            {
                ["Ai:Provider"] = "LocalOpenAI",
                ["Ai:LocalOpenAI:BaseUrl"] = $"http://127.0.0.1:{port}/v1",
                ["Ai:LocalOpenAI:Model"] = Model,
            },
        };

        using HttpClient client = api.CreateClient();

        IActionExtractor extractor = api.Services.GetRequiredService<IActionExtractor>();

        ExtractionResult result = await extractor.ExtractAsync(
            new ExtractionRequest(fixture.Notes, new DateOnly(2026, 8, 31)),
            TestContext.Current.CancellationToken);

        listener.Stop();
        await serving;

        Assert.True(result.IsSucceeded, result.FailureReason);
        Assert.NotEmpty(result.Kept);
        Assert.Equal("LocalOpenAI", result.Metrics.Provider);
        Assert.Equal(Model, result.Metrics.Model);
    }

    /// <summary>
    /// AD-16 at the host: <c>Ai:Provider=LocalOpenAI</c> pointed at a port nothing listens on is a
    /// refusal to start, and the message names the provider and the <c>/models</c> URL it tried.
    /// <c>AiOptionsValidator</c> passes this configuration, so the refusal can only come from the
    /// provider probe.
    /// </summary>
    [Fact]
    public async Task An_unreachable_local_server_stops_the_host_before_it_serves()
    {
        string baseUrl = $"http://127.0.0.1:{ClosedPort()}/v1";

        await using TestApi api = new()
        {
            ConfigurationOverrides =
            {
                ["Ai:Provider"] = "LocalOpenAI",
                ["Ai:LocalOpenAI:BaseUrl"] = baseUrl,
                ["Ai:LocalOpenAI:Model"] = "any-model",
            },
        };

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Task.Run(() => api.CreateClient(), TestContext.Current.CancellationToken));

        Assert.Contains("LocalOpenAI", failure.Message, StringComparison.Ordinal);
        Assert.Contains($"{baseUrl}/models", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The AI check is registered before the seeder, so a misconfigured provider stops the host
    /// before any demo row is written. Hosted services start in registration order, so asserting
    /// that order is asserting the guarantee.
    /// </summary>
    [Fact]
    public void The_ai_startup_check_runs_before_the_seeder()
    {
        using TestApi api = new();

        string[] hostedServices =
        [
            .. api.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
                .Select(service => service.GetType().Name),
        ];

        int check = Array.IndexOf(hostedServices, nameof(AiStartupCheck));
        int probe = Array.IndexOf(hostedServices, nameof(ProviderStartupProbe));
        int seeder = Array.IndexOf(hostedServices, "DemoDataSeeder");

        Assert.True(check >= 0, $"The host registers no {nameof(AiStartupCheck)}. Hosted services: [{string.Join(", ", hostedServices)}].");
        Assert.True(probe >= 0, $"The host registers no {nameof(ProviderStartupProbe)}. Hosted services: [{string.Join(", ", hostedServices)}].");
        Assert.True(seeder >= 0, $"The host registers no DemoDataSeeder. Hosted services: [{string.Join(", ", hostedServices)}].");
        Assert.True(check < seeder, "AD-16 — the AI check must start before the seeder, so a bad provider fails before any row is written.");
        Assert.True(probe < seeder, "AD-16 — the provider probe must start before the seeder, so an unreachable model server fails before any row is written.");
    }

    /// <summary>
    /// A minimal OpenAI-compatible server: <c>/models</c> lists <paramref name="model"/>, and
    /// <c>/chat/completions</c> answers with <paramref name="content"/>. Runs until the listener stops.
    /// </summary>
    private static async Task ServeAsync(HttpListener listener, string model, string content)
    {
        while (true)
        {
            HttpListenerContext context;

            try
            {
                context = await listener.GetContextAsync();
            }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            JsonObject body = context.Request.Url!.AbsolutePath.EndsWith("/models", StringComparison.Ordinal)
                ? new JsonObject
                {
                    ["object"] = "list",
                    ["data"] = new JsonArray(new JsonObject { ["id"] = model, ["object"] = "model", ["owned_by"] = "stub" }),
                }
                : new JsonObject
                {
                    ["id"] = "chatcmpl-stub",
                    ["object"] = "chat.completion",
                    ["created"] = 1_760_000_000,
                    ["model"] = model,
                    ["choices"] = new JsonArray(new JsonObject
                    {
                        ["index"] = 0,
                        ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = content },
                        ["finish_reason"] = "stop",
                    }),
                    ["usage"] = new JsonObject { ["prompt_tokens"] = 1, ["completion_tokens"] = 1, ["total_tokens"] = 2 },
                };

            byte[] bytes = Encoding.UTF8.GetBytes(body.ToJsonString());

            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }
    }

    private static int ClosedPort()
    {
        System.Net.Sockets.TcpListener probe = new(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        int port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        return port;
    }
}
