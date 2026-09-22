namespace ActionLedger.Infrastructure.Ai.Providers;

/// <summary>
/// The validated <c>Ai:AzureOpenAI</c> values, handed in by the composition root. The api key is a
/// secret: it lives here, beside the factory that sends it, and never in <see cref="AiSettings"/>.
/// </summary>
/// <remarks>
/// <see cref="ToString"/> is overridden because a record prints every member by default, and a
/// settings record is exactly the kind of value that ends up in a log line or an exception message.
/// </remarks>
/// <param name="Endpoint">
/// The Azure OpenAI v1 endpoint, <c>https://&lt;resource&gt;.openai.azure.com/openai/v1/</c>.
/// </param>
/// <param name="Model">The deployment name the requests ask for.</param>
/// <param name="ApiKey">The resource key, sent as the bearer credential. A secret.</param>
public sealed record AzureOpenAISettings(string Endpoint, string Model, string ApiKey)
{
    /// <summary>The endpoint and the model, and whether a key is present — never the key itself.</summary>
    public override string ToString() =>
        $"{nameof(AzureOpenAISettings)} {{ Endpoint = {Endpoint}, Model = {Model}, ApiKey = {(string.IsNullOrWhiteSpace(ApiKey) ? "<missing>" : "<redacted>")} }}";
}
