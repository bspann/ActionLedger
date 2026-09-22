using Microsoft.Extensions.AI;

namespace ActionLedger.Infrastructure.Ai.Providers;

/// <summary>
/// AD-21 — the Fake provider's factory, and the only one this story registers.
/// </summary>
/// <remarks>
/// The Fake is what all of Sunday's work, CI, and the demo fallback run on: it answers from the
/// committed fixture catalog with no model server, no credential, and no network. Story 2.7 adds
/// <c>LocalOpenAIChatClientFactory</c> and <c>AzureOpenAIChatClientFactory</c> beside this file and
/// changes nothing else.
/// </remarks>
/// <param name="catalog">The embedded answer table.</param>
public sealed class FakeChatClientFactory(FixtureCatalog catalog) : IChatClientFactory
{
    /// <summary>
    /// The <c>Ai:Provider</c> value this factory answers to. The Api's <c>AiOptions.FakeProvider</c>
    /// holds the same literal; AD-1 Rule 4 forbids this ring seeing the Api, so the string is
    /// repeated rather than shared, and <c>ComposeTopologyTests</c> already pins
    /// <c>Ai__Provider=Fake</c> in <c>.env.example</c>.
    /// </summary>
    public const string ProviderName = "Fake";

    /// <summary>
    /// What the Fake reports as its model. Run Detail renders this on every seeded and every
    /// demo-fallback run, so it is a real, stable string rather than a blank or a placeholder.
    /// </summary>
    public const string ModelName = "fixture-catalog";

    /// <inheritdoc />
    public string Provider => ProviderName;

    /// <inheritdoc />
    public string Model => ModelName;

    /// <inheritdoc />
    public IChatClient Create() => new FakeChatClient(catalog);
}
