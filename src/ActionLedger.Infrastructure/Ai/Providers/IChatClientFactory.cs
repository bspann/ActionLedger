using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace ActionLedger.Infrastructure.Ai.Providers;

/// <summary>
/// FR-7, AD-11 — one provider is one class in this folder plus one DI registration line.
/// </summary>
/// <remarks>
/// <para>
/// Factories are registered keyed by their <see cref="Provider"/> name and resolved through
/// <c>Ai:Provider</c>. Story 2.7's whole proof point is the size of its diff: two classes here,
/// two <c>AddKeyedSingleton</c> lines in <c>InfrastructureRegistration</c>, and configuration.
/// Nothing above this folder learns a provider's name.
/// </para>
/// <para>
/// A <c>switch</c> in the registration method would work identically today and would have to be
/// edited by Story 2.7 — which is exactly the edit the proof point claims is unnecessary. Keyed DI
/// also turns an unregistered provider into a startup failure that names it, rather than a null
/// reference at the first extraction (AD-16).
/// </para>
/// </remarks>
public interface IChatClientFactory
{
    /// <summary>The <c>Ai:Provider</c> value this factory answers to. Its own keyed-DI key.</summary>
    string Provider { get; }

    /// <summary>
    /// The model this provider will answer with, for Run Detail and
    /// <c>GET /api/v1/ai/provider</c>. Always a real, stable string — never blank.
    /// </summary>
    string Model { get; }

    /// <summary>Builds the chat client the single extractor talks to.</summary>
    IChatClient Create();
}

/// <summary>
/// The one place an <see cref="IChatClientFactory"/> is looked up by name, so the composition root
/// and <c>AiStartupCheck</c> refuse an unregistered provider with the same words.
/// </summary>
/// <remarks>
/// Two copies of this message drifted apart once already: the registration's omitted the list of
/// providers that do have a factory, which is the half that says what to set instead of only what
/// is wrong. One method, one message.
/// </remarks>
internal static class ChatClientFactories
{
    /// <summary>The factory keyed by <paramref name="provider"/>.</summary>
    /// <exception cref="InvalidOperationException">Nothing is registered under that name.</exception>
    internal static IChatClientFactory Resolve(IServiceProvider services, string provider) =>
        services.GetKeyedService<IChatClientFactory>(provider) ?? throw new InvalidOperationException(NotRegistered(services, provider));

    /// <summary>Why <paramref name="provider"/> cannot be served, and what can be.</summary>
    internal static string NotRegistered(IServiceProvider services, string provider) =>
        $"Ai:Provider '{provider}' has no registered IChatClientFactory. "
        + $"Registered providers: [{string.Join(", ", Registered(services))}]. "
        + "FR-7 makes adding one a factory class in Infrastructure/Ai/Providers plus one "
        + "AddKeyedSingleton line in AddActionLedgerAi.";

    /// <summary>The provider names that do have a factory.</summary>
    private static IEnumerable<string> Registered(IServiceProvider services) =>
        services
            .GetKeyedServices<IChatClientFactory>(KeyedService.AnyKey)
            .Select(factory => factory.Provider)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
}
