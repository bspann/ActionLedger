using System.Reflection;
using System.Text;

namespace ActionLedger.Infrastructure.Ai;

/// <summary>
/// The one reader over this assembly's embedded prompt and fixture resources (AD-6, AD-21).
/// </summary>
/// <remarks>
/// Two consumers — <see cref="PromptCatalog"/> and <see cref="FixtureCatalog"/> — and one reader,
/// so a mistake in a logical name surfaces in one place with one message rather than as two
/// different empty catalogs. The logical names are set by
/// <c>ActionLedger.Infrastructure.csproj</c>: <c>prompts/&lt;file&gt;</c> and
/// <c>fixtures/extraction/&lt;file&gt;</c>, forward slashes and all.
/// </remarks>
internal static class EmbeddedContent
{
    private static readonly Assembly Assembly = typeof(InfrastructureAssemblyMarker).Assembly;

    /// <summary>
    /// The resource's text, decoded as UTF-8. <c>.gitattributes</c> pins <c>fixtures/**</c> and
    /// <c>prompts/**</c> to LF, so the bytes here are the bytes on any checkout — which is what
    /// makes the catalog's hashes and the excerpt search stable on Windows.
    /// </summary>
    /// <param name="logicalName">The full logical name, for example <c>prompts/extract-actions.v1.md</c>.</param>
    /// <exception cref="InvalidOperationException">No resource has that name.</exception>
    internal static string Read(string logicalName)
    {
        using Stream? resource = Assembly.GetManifestResourceStream(logicalName);

        if (resource is null)
        {
            throw new InvalidOperationException(
                $"'{logicalName}' is not an embedded resource of {Assembly.GetName().Name}. "
                + $"Embedded: [{string.Join(", ", Names().Order(StringComparer.Ordinal))}].");
        }

        using StreamReader reader = new(resource, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        return reader.ReadToEnd();
    }

    /// <summary>Every embedded logical name that starts with <paramref name="prefix"/>.</summary>
    /// <param name="prefix">A logical-name prefix, for example <c>fixtures/extraction/</c>.</param>
    internal static IReadOnlyList<string> NamesUnder(string prefix) =>
        [.. Names().Where(name => name.StartsWith(prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal)];

    private static string[] Names() => Assembly.GetManifestResourceNames();
}
