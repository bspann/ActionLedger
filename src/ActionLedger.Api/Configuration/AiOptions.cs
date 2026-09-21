using System.ComponentModel.DataAnnotations;

namespace ActionLedger.Api.Configuration;

/// <summary>
/// The <c>Ai</c> section (AD-16). Bound and validated at startup; a missing required key fails
/// the host with a message naming the key.
/// </summary>
/// <remarks>
/// Only the keys the spine's Config keys row lists appear here. The provider-specific reachability
/// probe AD-16 describes — <c>GET {BaseUrl}/models</c> for LocalOpenAI, credential presence for
/// AzureOpenAI — is a startup hosted service that arrives with the extraction work in Epic 2.
/// What this story can enforce without an AI ring is the shape: the provider is one of the three
/// names, and the sub-section that provider needs is populated (<see cref="AiOptionsValidator"/>).
/// </remarks>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    public const string AzureOpenAIProvider = "AzureOpenAI";
    public const string LocalOpenAIProvider = "LocalOpenAI";
    public const string FakeProvider = "Fake";

    [Required(ErrorMessage = "Ai:Provider is required.")]
    [RegularExpression(
        $"^({AzureOpenAIProvider}|{LocalOpenAIProvider}|{FakeProvider})$",
        ErrorMessage = $"Ai:Provider must be one of {AzureOpenAIProvider}, {LocalOpenAIProvider}, {FakeProvider}.")]
    public string Provider { get; init; } = string.Empty;

    [Required(ErrorMessage = "Ai:PromptVersion is required.")]
    public string PromptVersion { get; init; } = string.Empty;

    [Range(1, 600, ErrorMessage = "Ai:CallTimeoutSeconds must be between 1 and 600.")]
    public int CallTimeoutSeconds { get; init; } = 90;

    [Range(0.0, 1.0, ErrorMessage = "Ai:LowConfidenceThreshold must be between 0.0 and 1.0.")]
    public double LowConfidenceThreshold { get; init; } = 0.70;

    public LocalOpenAIOptions LocalOpenAI { get; init; } = new();

    public AzureOpenAIOptions AzureOpenAI { get; init; } = new();

    /// <summary>The <c>Ai:LocalOpenAI</c> sub-section — LM Studio or Ollama over the OpenAI wire format.</summary>
    public sealed class LocalOpenAIOptions
    {
        public string BaseUrl { get; init; } = string.Empty;

        public string Model { get; init; } = string.Empty;
    }

    /// <summary>The <c>Ai:AzureOpenAI</c> sub-section. <c>ApiKey</c> is a secret: environment or user secrets only.</summary>
    public sealed class AzureOpenAIOptions
    {
        public string Endpoint { get; init; } = string.Empty;

        public string Model { get; init; } = string.Empty;

        public string ApiKey { get; init; } = string.Empty;
    }
}
