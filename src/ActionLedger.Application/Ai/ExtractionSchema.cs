using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ActionLedger.Application.Ai;

/// <summary>
/// AD-6, AD-11 — the committed extraction schema, read once out of this assembly. One file, two
/// views: <see cref="Version"/> is what every Extraction Run records as its Schema Version, and
/// <see cref="WireJson"/> is the document a provider's structured-output request is built from.
/// </summary>
/// <remarks>
/// <para>
/// AD-6 makes <c>SchemaVersion</c> the schema file's own top-level <c>version</c> member, which is
/// the only way a stored run can say which document validated it. <c>version</c> is not a JSON
/// Schema keyword, and AD-11 requires the wire schema to be a strict subset of the committed file,
/// so <see cref="WireJson"/> is the same document with that one member removed — a strict-mode
/// server may reject an unknown top-level member outright. There is no second copy of the schema
/// anywhere in the solution.
/// </para>
/// <para>
/// The resource is embedded by <c>ActionLedger.Application.csproj</c>, so every consumer — the api
/// image, the tests, and the Evaluation Gate — reads the same bytes with no path configuration and
/// no copy step.
/// </para>
/// </remarks>
public static class ExtractionSchema
{
    /// <summary>The manifest-resource name MSBuild gives <c>Ai/extract-actions.schema.json</c>.</summary>
    public const string ResourceName = "ActionLedger.Application.Ai.extract-actions.schema.json";

    /// <summary>The committed document's top-level version member. Not a JSON Schema keyword.</summary>
    private const string VersionMember = "version";

    private static readonly string CommittedJson = ReadCommittedJson();

    private static readonly JsonObject Committed = JsonNode.Parse(CommittedJson)?.AsObject()
        ?? throw new InvalidOperationException($"{ResourceName} is not a JSON object.");

    /// <summary>
    /// The schema's own top-level <c>version</c>, recorded on every Extraction Run (AD-6).
    /// </summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>
    /// The committed document exactly as it is on disk, including <c>version</c>. This is what the
    /// parity test and the Evaluation Gate read; it is never sent to a provider.
    /// </summary>
    public static string CommittedJsonText => CommittedJson;

    /// <summary>
    /// The wire view: the committed document with <c>version</c> removed, and nothing else changed.
    /// <c>ChatOptions.ResponseFormat</c> is built from this (AD-11).
    /// </summary>
    public static JsonElement WireJson { get; } = BuildWireJson();

    /// <summary>
    /// The top-level <c>version</c> as a string. <c>TryGetValue</c> rather than
    /// <c>GetValue&lt;string&gt;</c>, because a <c>version</c> that is a number or a bool would
    /// otherwise throw an <c>InvalidOperationException</c> System.Text.Json wrote from inside a
    /// static initializer, and the crafted AD-6 message below would never reach anyone.
    /// </summary>
    private static string ReadVersion()
    {
        if (Committed[VersionMember] is JsonValue value && value.TryGetValue(out string? version) && version is not null)
        {
            return version;
        }

        throw new InvalidOperationException(
            $"{ResourceName} declares no top-level '{VersionMember}' member as a JSON string. "
            + "AD-6 makes it the Schema Version of every run.");
    }

    private static string ReadCommittedJson()
    {
        using Stream? resource = typeof(ExtractionSchema).Assembly.GetManifestResourceStream(ResourceName);

        if (resource is null)
        {
            string embedded = string.Join(
                ", ",
                typeof(ExtractionSchema).Assembly.GetManifestResourceNames().Order(StringComparer.Ordinal));

            throw new InvalidOperationException(
                $"{ResourceName} is not embedded in {typeof(ExtractionSchema).Assembly.GetName().Name}. Embedded: [{embedded}].");
        }

        using StreamReader reader = new(resource, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        return reader.ReadToEnd();
    }

    private static JsonElement BuildWireJson()
    {
        JsonObject wire = JsonNode.Parse(CommittedJson)!.AsObject();

        wire.Remove(VersionMember);

        // A JsonElement rather than a JsonNode: ChatResponseFormat.ForJsonSchema takes one, and a
        // JsonDocument parsed here is owned by this static and outlives every request that reads it.
        return JsonDocument.Parse(wire.ToJsonString()).RootElement.Clone();
    }
}
