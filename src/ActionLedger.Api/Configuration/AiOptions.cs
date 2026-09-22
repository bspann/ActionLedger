using System.ComponentModel.DataAnnotations;

namespace ActionLedger.Api.Configuration;

/// <summary>
/// The <c>Ai</c> section (AD-16). Bound and validated at startup; a missing required key fails
/// the host with a message naming the key.
/// </summary>
/// <remarks>
/// Only the keys the spine's Config keys row lists appear here. What this class enforces is the
/// shape: the provider is one of the three names, and the active provider's sub-section is
/// populated with an absolute URL and a model name that fits the run record
/// (<see cref="AiOptionsValidator"/>). The provider-specific probe AD-16 describes —
/// <c>GET {BaseUrl}/models</c> for LocalOpenAI, credential presence for AzureOpenAI — is
/// Infrastructure's <c>ProviderStartupProbe</c>, which runs after this validation.
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

    /// <summary>
    /// NFR-1 — seconds allowed for one provider call. At most 90, because a run makes at most two
    /// calls and must fit inside the 180-second run ceiling (DW-16).
    /// </summary>
    [Range(1, MaxCallTimeoutSeconds, ErrorMessage = "Ai:CallTimeoutSeconds must be between 1 and 90, so two calls fit inside the 180-second run ceiling.")]
    public int CallTimeoutSeconds { get; init; } = 90;

    /// <summary>The per-call ceiling: half of NFR-1's 180-second run ceiling.</summary>
    public const int MaxCallTimeoutSeconds = 90;

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
