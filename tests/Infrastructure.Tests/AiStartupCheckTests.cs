using ActionLedger.Application.Abstractions;
using ActionLedger.Infrastructure;
using ActionLedger.Infrastructure.Ai;
using ActionLedger.Infrastructure.Ai.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace ActionLedger.Infrastructure.Tests;

/// <summary>
/// AD-16 — the AI ring's fail-fast. A prompt version with no file, a provider with no factory, or a
/// provider whose probe fails stops the host at start rather than at the first extraction.
/// </summary>
/// <remarks>
/// The check is exercised through the container <c>AddActionLedgerAi</c> builds, so what is
/// asserted is the registration a host would get — not a hand-assembled one. Whether a host
/// propagates a hosted service's <c>StartAsync</c> failure is ASP.NET Core's own contract; what
/// belongs here is that the check refuses, and that its message names what is wrong.
/// </remarks>
public sealed class AiStartupCheckTests
{
    [Fact]
    public async Task A_valid_configuration_starts()
    {
        await using ServiceProvider services = Provider(new AiSettings(FakeChatClientFactory.ProviderName, "v1", 90, 0.70));

        await Started(services);
    }

    /// <summary>
    /// AD-16 — the check, then the provider probe, and nothing else. Hosted services start in
    /// registration order, so the order here is the order a host runs them in.
    /// </summary>
    [Fact]
    public void The_check_and_then_the_provider_probe_are_the_hosted_services()
    {
        using ServiceProvider services = Provider(new AiSettings(FakeChatClientFactory.ProviderName, "v1", 90, 0.70));

        Assert.Equal(
            [typeof(AiStartupCheck), typeof(ProviderStartupProbe)],
            services.GetServices<IHostedService>().Select(service => service.GetType()));
    }

    /// <summary>AD-6 — the message names the version asked for and lists what is embedded.</summary>
    [Fact]
    public async Task A_prompt_version_with_no_embedded_file_refuses_to_start()
    {
        await using ServiceProvider services = Provider(new AiSettings(FakeChatClientFactory.ProviderName, "v99", 90, 0.70));

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Started(services));

        Assert.Contains("v99", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("v1", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// FR-7 — the message names the provider and every one that does have a factory. Listing them
    /// constructs every registered factory, with empty sub-sections, which is what this also proves
    /// the real factories survive.
    /// </summary>
    [Fact]
    public async Task A_provider_with_no_registered_factory_refuses_to_start()
    {
        await using ServiceProvider services = Provider(new AiSettings("Nonexistent", "v1", 90, 0.70));

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Started(services));

        Assert.Contains("Nonexistent", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("[AzureOpenAI, Fake, LocalOpenAI]", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>AD-16 — an unreachable local server fails the start through the provider probe.</summary>
    [Fact]
    public async Task An_unreachable_local_server_refuses_to_start()
    {
        string baseUrl = $"http://127.0.0.1:{ClosedPort()}/v1";

        await using ServiceProvider services = Provider(
            new AiSettings(LocalOpenAIChatClientFactory.ProviderName, "v1", 90, 0.70),
            new LocalOpenAISettings(baseUrl, "any-model"));

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Started(services));

        Assert.Contains("LocalOpenAI", thrown.Message, StringComparison.Ordinal);
        Assert.Contains($"{baseUrl}/models", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>AD-16 — a missing Azure credential fails the start through the provider probe, naming the key.</summary>
    [Fact]
    public async Task A_missing_azure_key_refuses_to_start()
    {
        await using ServiceProvider services = Provider(
            new AiSettings(AzureOpenAIChatClientFactory.ProviderName, "v1", 90, 0.70),
            azure: new AzureOpenAISettings("https://example-resource.openai.azure.com/openai/v1/", "gpt-4o-mini", string.Empty));

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Started(services));

        Assert.Contains("Ai:AzureOpenAI:ApiKey", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>A complete Azure section resolves the whole seam and starts with no network.</summary>
    [Fact]
    public async Task A_complete_azure_section_starts_and_reports_its_model()
    {
        await using ServiceProvider services = Provider(
            new AiSettings(AzureOpenAIChatClientFactory.ProviderName, "v1", 90, 0.70),
            azure: new AzureOpenAISettings("https://example-resource.openai.azure.com/openai/v1/", "gpt-4o-mini", "not-a-real-key"));

        await Started(services);

        Assert.IsType<ChatClientActionExtractor>(services.GetRequiredService<IActionExtractor>());

        IAiProviderInfo info = services.GetRequiredService<IAiProviderInfo>();

        Assert.Equal("AzureOpenAI", info.Provider);
        Assert.Equal("gpt-4o-mini", info.Model);
    }

    /// <summary>The seam the rest of the epic resolves: one extractor, one provider-info port.</summary>
    [Fact]
    public void The_registration_resolves_the_whole_seam()
    {
        using ServiceProvider services = Provider(new AiSettings(FakeChatClientFactory.ProviderName, "v1", 90, 0.70));

        Assert.IsType<ChatClientActionExtractor>(services.GetRequiredService<IActionExtractor>());

        IAiProviderInfo info = services.GetRequiredService<IAiProviderInfo>();

        Assert.Equal(FakeChatClientFactory.ProviderName, info.Provider);
        Assert.Equal(FakeChatClientFactory.ModelName, info.Model);
    }

    [Fact]
    public void An_unregistered_provider_also_refuses_to_build_a_chat_client()
    {
        using ServiceProvider services = Provider(new AiSettings("Nonexistent", "v1", 90, 0.70));

        InvalidOperationException thrown =
            Assert.Throws<InvalidOperationException>(services.GetRequiredService<IActionExtractor>);

        Assert.Contains("Nonexistent", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>Every hosted service, in registration order — the order a host starts them in.</summary>
    private static async Task Started(ServiceProvider services)
    {
        foreach (IHostedService service in services.GetServices<IHostedService>())
        {
            await service.StartAsync(CancellationToken.None);
        }
    }

    private static ServiceProvider Provider(
        AiSettings settings,
        LocalOpenAISettings? local = null,
        AzureOpenAISettings? azure = null)
    {
        ServiceCollection services = new();

        services.AddLogging();
        services.AddActionLedgerAi(
            _ => settings,
            _ => local ?? new LocalOpenAISettings(string.Empty, string.Empty),
            _ => azure ?? new AzureOpenAISettings(string.Empty, string.Empty, string.Empty));

        return services.BuildServiceProvider();
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
