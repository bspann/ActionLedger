using ActionLedger.Application.Abstractions;
using ActionLedger.Infrastructure.Ai.Providers;

namespace ActionLedger.Infrastructure.Ai;

/// <summary>
/// AD-11 — the active provider and model, read off the configuration and the factory that was
/// actually resolved rather than off a second copy of either.
/// </summary>
/// <remarks>
/// The two values disagree about where they come from on purpose. <c>Provider</c> is the
/// configured <c>Ai:Provider</c>, so it names what an operator set; <c>Model</c> is what the
/// factory reports, so it names what will actually answer. Story 2.6 serves both from
/// <c>GET /api/v1/ai/provider</c> and Meeting Detail's progress caption reads them while a run is
/// in flight.
/// </remarks>
/// <param name="settings">The validated <c>Ai</c> values.</param>
/// <param name="factory">The factory the keyed registry resolved for <c>Ai:Provider</c>.</param>
public sealed class AiProviderInfo(AiSettings settings, IChatClientFactory factory) : IAiProviderInfo
{
    /// <inheritdoc />
    public string Provider => settings.Provider;

    /// <inheritdoc />
    public string Model => factory.Model;
}
