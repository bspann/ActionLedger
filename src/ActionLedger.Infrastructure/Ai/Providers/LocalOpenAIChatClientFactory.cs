using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using OpenAI.Models;

namespace ActionLedger.Infrastructure.Ai.Providers;

/// <summary>
/// FR-7 — LM Studio or Ollama through their OpenAI-compatible endpoint. The OpenAI SDK pointed at
/// <c>Ai:LocalOpenAI:BaseUrl</c>; nothing above this folder knows it is a local model.
/// </summary>
/// <remarks>
/// <para>
/// The constructor only stores values. <c>ChatClientFactories.Registered</c> builds every factory
/// to list them, including this one when the Fake is active and <c>Ai:LocalOpenAI</c> is empty, so
/// a URL is parsed only when a client is built or the server is probed.
/// </para>
/// <para>
/// AD-16 — <see cref="VerifyAsync"/> is the startup probe: <c>GET {BaseUrl}/models</c> under a
/// fixed ten-second timeout, and the configured model must be one the server lists.
/// </para>
/// </remarks>
/// <param name="settings">The validated <c>Ai</c> values, for the per-call budget.</param>
/// <param name="local">The validated <c>Ai:LocalOpenAI</c> values.</param>
public sealed class LocalOpenAIChatClientFactory(AiSettings settings, LocalOpenAISettings local) : IChatClientFactory
{
    /// <summary>The <c>Ai:Provider</c> value this factory answers to. The Api's <c>AiOptions.LocalOpenAIProvider</c> holds the same literal.</summary>
    public const string ProviderName = "LocalOpenAI";

    /// <summary>
    /// The api key sent to a local server. LM Studio and Ollama ignore it; the SDK refuses to build
    /// a client without one. It is not a credential.
    /// </summary>
    internal const string PlaceholderApiKey = "local-server-ignores-this-key";

    /// <summary>How long the startup probe waits for <c>/models</c>. Fixed: it is a reachability check, not a model call.</summary>
    internal static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

    /// <summary>How many of the server's model ids a "model not listed" message names.</summary>
    internal const int ListedModelLimit = 20;

    /// <inheritdoc />
    public string Provider => ProviderName;

    /// <inheritdoc />
    public string Model => local.Model;

    /// <inheritdoc />
    public string? EndpointHost => OpenAIWire.AbsoluteUri(local.BaseUrl, Uri.UriSchemeHttp, Uri.UriSchemeHttps)?.Authority;

    /// <inheritdoc />
    public IChatClient Create() =>
        new ChatClient(
                local.Model,
                new ApiKeyCredential(PlaceholderApiKey),
                OpenAIWire.Options(Endpoint(), OpenAIWire.ChatNetworkTimeout(settings)))
            .AsIChatClient();

    /// <inheritdoc />
    public async Task VerifyAsync(CancellationToken cancellationToken)
    {
        Uri endpoint = Endpoint();
        string modelsUrl = ModelsUrl(endpoint);

        OpenAIModelClient client = new(
            new ApiKeyCredential(PlaceholderApiKey),
            OpenAIWire.Options(endpoint, ProbeTimeout + OpenAIWire.NetworkTimeoutMargin));

        using CancellationTokenSource timeout = new(ProbeTimeout);
        using CancellationTokenSource probe = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        OpenAIModelCollection models;

        try
        {
            models = (await client.GetModelsAsync(probe.Token).ConfigureAwait(false)).Value;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The host is shutting down, not the server failing. Let the host see its own cancellation.
            throw;
        }
        catch (OperationCanceledException)
        {
            throw Failure($"GET {modelsUrl} did not answer within {ProbeTimeout.TotalSeconds:0} seconds.");
        }
        catch (ClientResultException exception) when (exception.Status != 0)
        {
            throw Failure($"GET {modelsUrl} answered HTTP {exception.Status}.");
        }
        catch (Exception exception)
        {
            // A refused connection, a DNS failure, a body that is not JSON. The SDK's message
            // carries no credential: the placeholder key travels in a header, never in the text.
            throw Failure($"GET {modelsUrl} is unreachable: {exception.GetType().Name}: {exception.Message}");
        }

        string[] ids =
        [
            .. models
                .Select(model => model.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal),
        ];

        // A server that lists nothing is not proof the model is missing — some builds load models
        // lazily — so only a non-empty list that lacks the model is a refusal.
        if (ids.Length > 0 && !ids.Contains(local.Model, StringComparer.Ordinal))
        {
            string listed = string.Join(", ", ids.Take(ListedModelLimit));
            string more = ids.Length > ListedModelLimit ? $" and {ids.Length - ListedModelLimit} more" : string.Empty;

            throw Failure(
                $"Ai:LocalOpenAI:Model '{local.Model}' is not among the models GET {modelsUrl} lists: [{listed}]{more}. "
                + "Set Ai:LocalOpenAI:Model to one of them, or load that model in LM Studio or pull it in Ollama.");
        }
    }

    /// <summary>The URL the SDK asks for the model list, for the messages that name it.</summary>
    internal static string ModelsUrl(Uri endpoint) => endpoint.AbsoluteUri.TrimEnd('/') + "/models";

    private Uri Endpoint() =>
        OpenAIWire.AbsoluteUri(local.BaseUrl, Uri.UriSchemeHttp, Uri.UriSchemeHttps)
        ?? throw Failure($"Ai:LocalOpenAI:BaseUrl '{local.BaseUrl}' is not an absolute http or https URL.");

    private static InvalidOperationException Failure(string problem) =>
        new(
            $"Ai:Provider is {ProviderName}, and the provider probe failed. {problem} "
            + "Start LM Studio's server or Ollama, check Ai:LocalOpenAI:BaseUrl (from a container the host is "
            + "host.docker.internal), or set Ai:Provider=Fake to run without a model server.");
}
