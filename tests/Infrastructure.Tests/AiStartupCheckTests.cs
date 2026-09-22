using ActionLedger.Application.Abstractions;
using ActionLedger.Infrastructure;
using ActionLedger.Infrastructure.Ai;
using ActionLedger.Infrastructure.Ai.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace ActionLedger.Infrastructure.Tests;

/// <summary>
/// AD-16 — the AI ring's fail-fast. A prompt version with no file, or a provider with no factory,
/// stops the host at start rather than at the first extraction.
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
        await using ServiceProvider services = Provider(new AiSettings(FakeChatClientFactory.ProviderName, "v1", 90));

        IHostedService check = Assert.Single(services.GetServices<IHostedService>());

        await check.StartAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>AD-6 — the message names the version asked for and lists what is embedded.</summary>
    [Fact]
    public async Task A_prompt_version_with_no_embedded_file_refuses_to_start()
    {
        await using ServiceProvider services = Provider(new AiSettings(FakeChatClientFactory.ProviderName, "v99", 90));

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Started(services));

        Assert.Contains("v99", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("v1", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>FR-7 — the message names the provider and the ones that do have a factory.</summary>
    [Theory]
    [InlineData("LocalOpenAI")]
    [InlineData("AzureOpenAI")]
    public async Task A_provider_with_no_registered_factory_refuses_to_start(string provider)
    {
        await using ServiceProvider services = Provider(new AiSettings(provider, "v1", 90));

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Started(services));

        Assert.Contains(provider, thrown.Message, StringComparison.Ordinal);
        Assert.Contains(FakeChatClientFactory.ProviderName, thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>The seam the rest of the epic resolves: one extractor, one provider-info port.</summary>
    [Fact]
    public void The_registration_resolves_the_whole_seam()
    {
        using ServiceProvider services = Provider(new AiSettings(FakeChatClientFactory.ProviderName, "v1", 90));

        Assert.IsType<ChatClientActionExtractor>(services.GetRequiredService<IActionExtractor>());

        IAiProviderInfo info = services.GetRequiredService<IAiProviderInfo>();

        Assert.Equal(FakeChatClientFactory.ProviderName, info.Provider);
        Assert.Equal(FakeChatClientFactory.ModelName, info.Model);
    }

    [Fact]
    public void An_unregistered_provider_also_refuses_to_build_a_chat_client()
    {
        using ServiceProvider services = Provider(new AiSettings("LocalOpenAI", "v1", 90));

        InvalidOperationException thrown =
            Assert.Throws<InvalidOperationException>(services.GetRequiredService<IActionExtractor>);

        Assert.Contains("LocalOpenAI", thrown.Message, StringComparison.Ordinal);
    }

    private static Task Started(ServiceProvider services) =>
        services.GetServices<IHostedService>().Single().StartAsync(CancellationToken.None);

    private static ServiceProvider Provider(AiSettings settings)
    {
        ServiceCollection services = new();

        services.AddLogging();
        services.AddActionLedgerAi(_ => settings);

        return services.BuildServiceProvider();
    }
}
