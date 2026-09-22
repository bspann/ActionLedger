using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ActionLedger.Infrastructure.Ai.Providers;

/// <summary>
/// AD-16 — the provider-specific startup probe: <c>GET {BaseUrl}/models</c> for LocalOpenAI,
/// credential presence for AzureOpenAI, nothing for the Fake.
/// </summary>
/// <remarks>
/// <para>
/// A hosted service of its own rather than a line in <c>AiStartupCheck</c>, so Story 2.7's diff
/// stays inside this folder, the DI registration, and configuration — the narrow diff is FR-7's
/// proof point. Each factory knows how to check itself through
/// <see cref="IChatClientFactory.VerifyAsync"/>; this class only asks the active one.
/// </para>
/// <para>
/// Registered directly after <c>AiStartupCheck</c> and before the seeder. Hosted services start in
/// registration order, so an unreachable model server fails the host before a demo row is written.
/// </para>
/// </remarks>
/// <param name="settings">The validated <c>Ai</c> values.</param>
/// <param name="services">The container, for the keyed factory lookup.</param>
/// <param name="logger">Logs what was verified — provider, model, endpoint host, and never a key.</param>
public sealed class ProviderStartupProbe(
    AiSettings settings,
    IServiceProvider services,
    ILogger<ProviderStartupProbe> logger) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        IChatClientFactory factory = ChatClientFactories.Resolve(services, settings.Provider);

        await factory.VerifyAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "AI provider verified. Provider {Provider}, model {Model}, endpoint host {EndpointHost}.",
            factory.Provider,
            factory.Model,
            factory.EndpointHost ?? "none");
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
