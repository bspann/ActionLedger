using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI.Chat;

namespace ActionLedger.Infrastructure.Ai.Providers;

/// <summary>
/// FR-7 — Azure OpenAI through its v1 endpoint. The same OpenAI SDK as
/// <see cref="LocalOpenAIChatClientFactory"/>, differing only in endpoint, model, and credential:
/// no Azure-specific SDK enters the tree.
/// </summary>
/// <remarks>
/// <para>
/// The key is sent as the SDK's bearer credential against
/// <c>https://&lt;resource&gt;.openai.azure.com/openai/v1/</c>. It is never logged and never put
/// into a message.
/// </para>
/// <para>
/// AD-16 — <see cref="VerifyAsync"/> is credential presence, not reachability: it re-asserts the
/// three values and an absolute https endpoint without a network call. <c>AiOptionsValidator</c>
/// already refuses the same things; this is defence in depth for a container built without the Api
/// (tests, the Evaluation Gate).
/// </para>
/// <para>
/// The constructor only stores values, so the factory is constructible with an empty sub-section
/// when another provider is active.
/// </para>
/// </remarks>
/// <param name="settings">The validated <c>Ai</c> values, for the per-call budget.</param>
/// <param name="azure">The validated <c>Ai:AzureOpenAI</c> values, api key included.</param>
public sealed class AzureOpenAIChatClientFactory(AiSettings settings, AzureOpenAISettings azure) : IChatClientFactory
{
    /// <summary>The <c>Ai:Provider</c> value this factory answers to. The Api's <c>AiOptions.AzureOpenAIProvider</c> holds the same literal.</summary>
    public const string ProviderName = "AzureOpenAI";

    /// <inheritdoc />
    public string Provider => ProviderName;

    /// <inheritdoc />
    public string Model => azure.Model;

    /// <inheritdoc />
    public string? EndpointHost => OpenAIWire.AbsoluteUri(azure.Endpoint, Uri.UriSchemeHttps)?.Authority;

    /// <inheritdoc />
    public IChatClient Create()
    {
        // Presence is re-asserted here too: a blank key would otherwise surface as an SDK argument
        // exception at the first extraction rather than as a message naming the key. The https
        // rule is the probe's and the Api validator's; a test stub on loopback http is still a
        // valid transport for the same request shape.
        Require("Ai:AzureOpenAI:Endpoint", azure.Endpoint);
        Require("Ai:AzureOpenAI:Model", azure.Model);
        Require("Ai:AzureOpenAI:ApiKey", azure.ApiKey);

        Uri endpoint = OpenAIWire.AbsoluteUri(azure.Endpoint, Uri.UriSchemeHttp, Uri.UriSchemeHttps) ?? Verified();

        return new ChatClient(
                azure.Model,
                new ApiKeyCredential(azure.ApiKey),
                OpenAIWire.Options(endpoint, OpenAIWire.ChatNetworkTimeout(settings)))
            .AsIChatClient();
    }

    /// <inheritdoc />
    public Task VerifyAsync(CancellationToken cancellationToken)
    {
        _ = Verified();

        return Task.CompletedTask;
    }

    /// <summary>The endpoint, once every required value is present.</summary>
    private Uri Verified()
    {
        Require("Ai:AzureOpenAI:Endpoint", azure.Endpoint);
        Require("Ai:AzureOpenAI:Model", azure.Model);
        Require("Ai:AzureOpenAI:ApiKey", azure.ApiKey);

        return OpenAIWire.AbsoluteUri(azure.Endpoint, Uri.UriSchemeHttps)
            ?? throw Failure(
                $"Ai:AzureOpenAI:Endpoint '{azure.Endpoint}' is not an absolute https URL. "
                + "Use https://<resource>.openai.azure.com/openai/v1/.");
    }

    private static void Require(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw Failure($"{key} is missing. Supply it from the environment or user secrets.");
        }
    }

    private static InvalidOperationException Failure(string problem) =>
        new($"Ai:Provider is {ProviderName}, and the provider check failed. {problem} Or set Ai:Provider=Fake to run without a credential.");
}
