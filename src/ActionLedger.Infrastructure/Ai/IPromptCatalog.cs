namespace ActionLedger.Infrastructure.Ai;

/// <summary>
/// AD-6 — the versioned prompt files, read out of this assembly rather than off disk.
/// </summary>
/// <remarks>
/// The interface lives beside its implementation in Infrastructure and not in
/// <c>Application/Abstractions</c>, although every other port in this repository lives there. No
/// Application code consumes a prompt: the extractor does, and the extractor is in this ring. AD-1
/// Rule 2's package allowlist makes an unused Application port dead weight, and a port with no
/// caller above it is a port in the wrong ring.
/// </remarks>
public interface IPromptCatalog
{
    /// <summary>
    /// The version a run uses: <c>Ai:PromptVersion</c> when it is set, else the highest <c>N</c>
    /// embedded (AD-6). Validated to exist at startup by <see cref="AiStartupCheck"/>.
    /// </summary>
    string Current { get; }

    /// <summary>Every embedded version, ascending by <c>N</c>.</summary>
    IReadOnlyList<string> Versions { get; }

    /// <summary>The prompt text for one version, byte-for-byte as the file holds it.</summary>
    /// <param name="version">A version label such as <c>v1</c>.</param>
    /// <exception cref="InvalidOperationException">
    /// No prompt is embedded for that version. The message names the version and lists what is.
    /// </exception>
    string Get(string version);
}
