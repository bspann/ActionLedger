namespace ActionLedger.Infrastructure.Ai.Providers;

/// <summary>
/// The validated <c>Ai:LocalOpenAI</c> values — LM Studio or Ollama over the OpenAI wire format —
/// handed in by the composition root. Infrastructure reads no configuration of its own.
/// </summary>
/// <remarks>
/// Both values are empty when another provider is active. The factory that holds this record is
/// still constructed then (<c>ChatClientFactories.Registered</c> builds every factory to list
/// them), so nothing here is checked until the factory is actually asked to reach the server.
/// </remarks>
/// <param name="BaseUrl">
/// The OpenAI-compatible base URL, for example <c>http://host.docker.internal:1234/v1</c> for LM
/// Studio or <c>http://host.docker.internal:11434/v1</c> for Ollama.
/// </param>
/// <param name="Model">The model id the server lists at <c>GET {BaseUrl}/models</c>.</param>
public sealed record LocalOpenAISettings(string BaseUrl, string Model);
