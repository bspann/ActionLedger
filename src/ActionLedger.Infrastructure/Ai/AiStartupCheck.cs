using ActionLedger.Application.Ai;
using ActionLedger.Infrastructure.Ai.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ActionLedger.Infrastructure.Ai;

/// <summary>
/// AD-16 — the AI ring's fail-fast. A prompt version with no file, or a provider with no factory,
/// stops the host before it serves a request rather than at the first extraction.
/// </summary>
/// <remarks>
/// <para>
/// This is <c>DemoDataSeeder</c>'s shape: a hosted service that re-asserts its invariant with an
/// <see cref="InvalidOperationException"/> rather than defaulting around it. The Api's
/// <c>ValidateOnStart</c> already checks that <c>Ai:Provider</c> is one of three names and that
/// <c>Ai:PromptVersion</c> is present; what it cannot know is whether a prompt file for that
/// version is embedded, or whether that provider has an implementation yet — both of which are
/// facts about this assembly.
/// </para>
/// <para>
/// It never touches the network. The provider-specific reachability probe AD-16 describes —
/// <c>GET {BaseUrl}/models</c> for LocalOpenAI, credential presence for AzureOpenAI — arrives with
/// the real providers in Story 2.7, and the Fake has nothing to reach.
/// </para>
/// <para>
/// Registered before the seeder, so a bad provider fails before any row is written: hosted services
/// start in registration order.
/// </para>
/// </remarks>
/// <param name="settings">The validated <c>Ai</c> values.</param>
/// <param name="prompts">The embedded prompt catalog (AD-6).</param>
/// <param name="services">The container, for the keyed factory lookup.</param>
/// <param name="logger">Logs what resolved, so a start says which provider and prompt won.</param>
public sealed class AiStartupCheck(
    AiSettings settings,
    IPromptCatalog prompts,
    IServiceProvider services,
    ILogger<AiStartupCheck> logger) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Throws with a message naming the version and listing what is embedded (AD-6).
        string promptVersion = prompts.Current;

        _ = prompts.Get(promptVersion);

        IChatClientFactory factory = ChatClientFactories.Resolve(services, settings.Provider);

        logger.LogInformation(
            "AI seam ready. Provider {Provider}, model {Model}, prompt {PromptVersion}, schema {SchemaVersion}.",
            settings.Provider,
            factory.Model,
            promptVersion,
            ExtractionSchema.Version);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
