using ActionLedger.Application.Abstractions;
using ActionLedger.Infrastructure.Ai;
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
/// <c>AiOptions</c>, which this story did not touch — so deleting the whole registration block from
/// <c>Program.cs</c> left the entire suite green. These two tests are what notices.
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
    /// AD-16 at the surface the acceptance criterion names — the <em>host</em>, not a hand-built
    /// container: a provider with no registered factory is a refusal to start naming the provider.
    /// </summary>
    /// <remarks>
    /// <c>AzureOpenAI</c> with a complete sub-section passes <c>AiOptionsValidator</c>, so the
    /// refusal can only come from <c>AiStartupCheck</c>. That is what makes this the host-level
    /// counterpart to the prompt-version test above, rather than a second reading of
    /// <c>AiOptions</c>.
    /// </remarks>
    [Fact]
    public async Task A_provider_with_no_registered_factory_stops_the_host_before_it_serves()
    {
        await using TestApi api = new()
        {
            ConfigurationOverrides =
            {
                ["Ai:Provider"] = "AzureOpenAI",
                ["Ai:AzureOpenAI:Endpoint"] = "https://example-resource.openai.azure.com/",
                ["Ai:AzureOpenAI:Model"] = "gpt-4o-mini",
                ["Ai:AzureOpenAI:ApiKey"] = "not-a-real-key",
            },
        };

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Task.Run(() => api.CreateClient(), TestContext.Current.CancellationToken));

        Assert.Contains("AzureOpenAI", failure.Message, StringComparison.Ordinal);
        Assert.Contains("Fake", failure.Message, StringComparison.Ordinal);
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
        int seeder = Array.IndexOf(hostedServices, "DemoDataSeeder");

        Assert.True(check >= 0, $"The host registers no {nameof(AiStartupCheck)}. Hosted services: [{string.Join(", ", hostedServices)}].");
        Assert.True(seeder >= 0, $"The host registers no DemoDataSeeder. Hosted services: [{string.Join(", ", hostedServices)}].");
        Assert.True(check < seeder, "AD-16 — the AI check must start before the seeder, so a bad provider fails before any row is written.");
    }
}
