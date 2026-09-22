using ActionLedger.Domain.Common;

namespace ActionLedger.Domain.Extraction;

/// <summary>
/// AD-6 — the measurements of the provider call an <see cref="ExtractionRun"/> records, whatever
/// its outcome.
/// </summary>
/// <remarks>
/// <para>
/// This is Domain's own shape for what the Application ring calls <c>ExtractionMetrics</c>. AD-1
/// forbids Domain seeing the Application assembly at all, so the two cannot be the same type; the
/// handler maps one onto the other in the single place that does it, and this type is what carries
/// the guards. Keeping the guard here rather than at the map means a seeded run and a live run are
/// held to the same rule.
/// </para>
/// <para>
/// Every member is required and non-null, including on a failed run. Tokens are <c>int</c> and are
/// <c>0</c> for the Fake provider, never null (FR-6): Run Detail renders "0" rather than a blank,
/// and a run that cannot be compared to another run is not reproducible.
/// </para>
/// <para>
/// <see cref="StartedAt"/> and <see cref="DurationMs"/> are the extractor's own measurement, not
/// <c>IClock</c>'s: the extractor is the only code that sees both ends of the provider call.
/// </para>
/// </remarks>
/// <param name="Provider">The configured <c>Ai:Provider</c> — <c>Fake</c>, <c>LocalOpenAI</c> or <c>AzureOpenAI</c>.</param>
/// <param name="Model">What the active provider reports as its model. <c>fixture-catalog</c> for the Fake.</param>
/// <param name="PromptVersion">The prompt revision the catalog resolved, for example <c>v1</c>.</param>
/// <param name="SchemaVersion">The committed extraction schema's own top-level <c>version</c>.</param>
/// <param name="StartedAt">When the first provider call began, in UTC.</param>
/// <param name="DurationMs">Milliseconds across every attempt, at most two.</param>
/// <param name="InputTokens">Prompt tokens summed over every attempt. <c>0</c> for the Fake.</param>
/// <param name="OutputTokens">Completion tokens summed over every attempt. <c>0</c> for the Fake.</param>
public sealed record ExtractionRunMetadata(
    string Provider,
    string Model,
    string PromptVersion,
    string SchemaVersion,
    DateTimeOffset StartedAt,
    int DurationMs,
    int InputTokens,
    int OutputTokens)
{
    /// <summary>The longest provider name. The three names AD-11 fixes are far shorter.</summary>
    public const int ProviderMaxLength = 50;

    /// <summary>The longest model identifier. Deployment names and local model ids both fit.</summary>
    public const int ModelMaxLength = 200;

    /// <summary>The longest prompt version, for example <c>v1</c>.</summary>
    public const int PromptVersionMaxLength = 32;

    /// <summary>The longest schema version, the committed schema's top-level <c>version</c>.</summary>
    public const int SchemaVersionMaxLength = 32;

    /// <summary>
    /// Refuses metadata that could not describe a real call: a blank name, a negative duration, or
    /// a negative token count. Called by <see cref="ExtractionRun.Start"/>, which is the only thing
    /// that persists one.
    /// </summary>
    /// <exception cref="DomainRuleException">A member is blank, too long, or negative.</exception>
    internal ExtractionRunMetadata Validated() =>
        new(
            RequireText(Provider, "provider", ProviderMaxLength),
            RequireText(Model, "model", ModelMaxLength),
            RequireText(PromptVersion, "prompt version", PromptVersionMaxLength),
            RequireText(SchemaVersion, "schema version", SchemaVersionMaxLength),
            StartedAt.ToUniversalTime(),
            RequireNotNegative(DurationMs, "duration"),
            RequireNotNegative(InputTokens, "input token count"),
            RequireNotNegative(OutputTokens, "output token count"));

    private static int RequireNotNegative(int value, string field) =>
        value < 0
            ? throw new DomainRuleException($"An Extraction Run's {field} cannot be negative.")
            : value;

    private static string RequireText(string value, string field, int maxLength)
    {
        string trimmed = (value ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            throw new DomainRuleException($"An Extraction Run's {field} is required.");
        }

        return trimmed.Length > maxLength
            ? throw new DomainRuleException($"An Extraction Run's {field} cannot exceed {maxLength} characters.")
            : trimmed;
    }
}
