using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace ActionLedger.Application.Ai;

/// <summary>
/// AD-11 — the wire shape of a provider's answer, deserialized strictly. One object whose only
/// member is <c>actions</c>, exactly as <c>extract-actions.schema.json</c> declares it.
/// </summary>
/// <remarks>
/// Strictness is the whole point of this type. <c>required</c> makes a missing member a
/// deserialization failure rather than a silent default, and
/// <see cref="JsonUnmappedMemberHandling.Disallow"/> makes an extra member one too — so a model
/// that invents a field fails the attempt instead of having it quietly dropped. This is layer one
/// of the three FR-5 requires; <see cref="ExtractionOutputValidator"/> is layer two and
/// <see cref="ExcerptVerifier"/> is the post-validation filter.
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ExtractionOutput
{
    /// <summary>Every commitment the model read out of the notes, in the order it returned them.</summary>
    public required IReadOnlyList<ExtractedAction> Actions { get; init; }

    /// <summary>
    /// The one <see cref="JsonSerializerOptions"/> the seam reads and writes extraction JSON with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Both directions use it.</strong> It deserializes a provider response, and it also
    /// serializes the two JSON documents the seam writes: the extractor's
    /// <c>{"meetingDate", "notes"}</c> user message and the Fake's heuristic answer. On those two
    /// outbound paths <c>UnmappedMemberHandling</c> and <c>ReadCommentHandling</c> mean nothing,
    /// while the naming policy and the encoder decide the bytes — so a change made for a
    /// deserialization need changes what the seam sends, and both directions have to be weighed
    /// before editing this object.
    /// </para>
    /// There is no shared options object anywhere in this ring, and a second one here would be a
    /// second contract: camelCase names, unmapped members disallowed, and no tolerance for trailing
    /// commas or comments, because a model that emits either is not emitting the committed schema.
    /// <c>RespectNullableAnnotations</c> is what makes <c>JsonSchemaExporter</c> type a
    /// non-nullable <c>string</c> as <c>"string"</c> and a <c>string?</c> as
    /// <c>["string","null"]</c>, which is the parity the committed file declares. The resolver is
    /// set explicitly because <c>JsonSchemaExporter</c> refuses options that have none, and the
    /// parity test reads the exported schema out of this very object.
    /// </remarks>
    public static JsonSerializerOptions SerializerOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        RespectNullableAnnotations = true,
        NumberHandling = JsonNumberHandling.Strict,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };
}

/// <summary>
/// One proposed action exactly as it arrived on the wire, before
/// <see cref="ExtractionOutputValidator"/> has said anything about it.
/// </summary>
/// <remarks>
/// <see cref="SuggestedDueDate"/> is a <c>string?</c> and deliberately not a <c>DateOnly?</c>.
/// Binding it to a date would make <c>System.Text.Json</c> reject <c>"2026-13-40"</c> before the
/// validator ever saw it, turning a date-format failure into a deserialization failure with a
/// worse message — and <c>JsonSchemaExporter</c> would emit a <c>format</c>-annotated
/// <c>"string"</c> rather than the <c>["string","null"]</c> the committed file declares. The
/// validator owns date format (FR-5).
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ExtractedAction
{
    /// <summary>What is to be done, as a short phrase. 1–500 characters.</summary>
    public required string Description { get; init; }

    /// <summary>
    /// The person the notes name, spelled as the notes spell them. Up to 100 characters and never
    /// <c>null</c>: the published schema types this member as a plain string, so an unstated owner
    /// is the empty string.
    /// </summary>
    public required string SuggestedOwner { get; init; }

    /// <summary><c>YYYY-MM-DD</c>, or <c>null</c> when the notes state no date.</summary>
    public required string? SuggestedDueDate { get; init; }

    /// <summary>How sure the model is that this is a real commitment. 0–1 inclusive.</summary>
    public required double Confidence { get; init; }

    /// <summary>The sentence the action comes from, copied verbatim. 1–1000 characters.</summary>
    public required string SourceExcerpt { get; init; }
}
