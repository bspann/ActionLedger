using System.Globalization;
using System.Text.RegularExpressions;

namespace ActionLedger.Infrastructure.Ai;

/// <summary>
/// AD-6 — <c>/prompts/extract-actions.v&lt;N&gt;.md</c>, embedded in this assembly so every
/// consumer reads the same bytes with no path configuration and no copy step.
/// </summary>
/// <remarks>
/// <para>
/// <c>PromptFileTests</c> allows nothing but <c>extract-actions.v&lt;N&gt;.md</c> under
/// <c>prompts/</c>, so the pattern below is the whole of the folder's shape rather than a guess at
/// it. <see cref="IPromptCatalog.Current"/> is the configured version when set, else the highest
/// <c>N</c> by numeric order — <c>v10</c> sorts after <c>v9</c>, which a string sort would get
/// wrong the first time a tenth revision lands.
/// </para>
/// <para>
/// A configured version that is not embedded is a startup failure, not a first-extraction failure
/// (AD-16). <see cref="AiStartupCheck"/> reads <see cref="Current"/> before anything serves, and
/// the message names the version and lists what is actually there — which is the difference
/// between a typo found in ten seconds and one found in the demo.
/// </para>
/// </remarks>
public sealed partial class PromptCatalog : IPromptCatalog
{
    /// <summary>The logical-name prefix <c>ActionLedger.Infrastructure.csproj</c> gives prompt files.</summary>
    private const string Prefix = "prompts/";

    private readonly IReadOnlyDictionary<string, string> _resourcesByVersion;
    private readonly string? _configuredVersion;

    /// <summary>Reads the embedded prompt files once.</summary>
    /// <param name="settings">The validated <c>Ai</c> values. Only <c>PromptVersion</c> is read.</param>
    public PromptCatalog(AiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _configuredVersion = string.IsNullOrWhiteSpace(settings.PromptVersion) ? null : settings.PromptVersion;

        Dictionary<string, string> resources = [];
        List<(int Number, string Version)> ordered = [];

        foreach (string name in EmbeddedContent.NamesUnder(Prefix))
        {
            Match match = PromptFileName().Match(name[Prefix.Length..]);

            // TryParse, not Parse: the filename rule allows any run of digits, so
            // `extract-actions.v99999999999.md` would otherwise throw an OverflowException while
            // building the catalog — out of a constructor, naming neither the file nor the reason.
            // A version whose N does not fit an int is not a version; it is left out, and asking
            // for it gets the crafted message below, which does name it.
            if (!match.Success
                || !int.TryParse(match.Groups["version"].Value[1..], NumberStyles.None, CultureInfo.InvariantCulture, out int number))
            {
                continue;
            }

            string version = match.Groups["version"].Value;

            resources[version] = name;
            ordered.Add((number, version));
        }

        _resourcesByVersion = resources;

        Versions = [.. ordered.OrderBy(entry => entry.Number).Select(entry => entry.Version)];
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Versions { get; }

    /// <inheritdoc />
    public string Current =>
        _configuredVersion
        ?? (Versions.Count > 0
            ? Versions[^1]
            : throw new InvalidOperationException(
                "No prompt is embedded in ActionLedger.Infrastructure. AD-6 requires at least one "
                + "'prompts/extract-actions.v<N>.md'."));

    /// <inheritdoc />
    public string Get(string version)
    {
        if (!_resourcesByVersion.TryGetValue(version, out string? resource))
        {
            throw new InvalidOperationException(
                $"Ai:PromptVersion '{version}' has no embedded prompt file. "
                + $"Embedded prompt versions: [{string.Join(", ", Versions)}]. "
                + "AD-6 requires the configured version to exist; add prompts/extract-actions."
                + $"{version}.md or set Ai:PromptVersion to one of the versions above.");
        }

        return EmbeddedContent.Read(resource);
    }

    [GeneratedRegex(@"\Aextract-actions\.(?<version>v\d+)\.md\z", RegexOptions.CultureInvariant)]
    private static partial Regex PromptFileName();
}
