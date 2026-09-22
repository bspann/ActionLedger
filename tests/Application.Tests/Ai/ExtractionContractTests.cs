using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using ActionLedger.Application.Ai;
using Xunit;

namespace ActionLedger.Application.Tests.Ai;

/// <summary>
/// AD-11 — the Application half of the extraction seam: the committed schema, strict
/// deserialization, the validator's every boundary, the one normalizer, and the one verifier.
/// </summary>
/// <remarks>
/// The extractor's own retry and filter matrix lives in <c>Infrastructure.Tests</c>, where its ring
/// is (AD-18). The epic's second acceptance criterion asks for those three response paths in
/// <c>Application.Tests</c>, but it places <c>ChatClientActionExtractor</c> in Infrastructure in the
/// same sentence and this project references only <c>ActionLedger.Application</c>.
/// </remarks>
public sealed class ExtractionContractTests
{
    // --- The committed schema (AD-6) ---------------------------------------------------------

    [Fact]
    public void The_schema_is_embedded_and_declares_its_own_version()
    {
        Assert.Contains(
            ExtractionSchema.ResourceName,
            typeof(ExtractionSchema).Assembly.GetManifestResourceNames());

        Assert.Equal("1", ExtractionSchema.Version);
    }

    [Fact]
    public void The_version_reported_is_the_version_the_committed_file_declares()
    {
        JsonObject committed = JsonNode.Parse(ExtractionSchema.CommittedJsonText)!.AsObject();

        Assert.Equal(committed["version"]!.GetValue<string>(), ExtractionSchema.Version);
    }

    [Fact]
    public void The_wire_schema_drops_version_and_changes_nothing_else()
    {
        JsonObject committed = JsonNode.Parse(ExtractionSchema.CommittedJsonText)!.AsObject();
        JsonObject wire = JsonNode.Parse(ExtractionSchema.WireJson.GetRawText())!.AsObject();

        Assert.False(wire.ContainsKey("version"));

        committed.Remove("version");

        Assert.Equal(committed.ToJsonString(), wire.ToJsonString());
    }

    // --- Exporter parity (AD-11) -------------------------------------------------------------

    /// <summary>
    /// The one assertion AD-11 names by name: what <c>JsonSchemaExporter</c> makes of
    /// <see cref="ExtractionOutput"/> agrees with the committed file on property names, the
    /// <c>required</c> set, and types.
    /// </summary>
    /// <remarks>
    /// Types are compared per property rather than over the whole document. The exporter types the
    /// root object and the array element as <c>["object","null"]</c> — a reference type is
    /// nullable to it wherever an annotation cannot say otherwise — which is not drift from the
    /// committed file so much as a different question being answered. What the committed file and
    /// the C# type must agree on is every leaf: in particular <c>suggestedOwner</c> is
    /// <c>"string"</c> and <c>suggestedDueDate</c> is <c>["string","null"]</c>.
    /// </remarks>
    [Fact]
    public void The_exported_schema_matches_the_committed_file_on_names_required_and_types()
    {
        JsonObject exported = ExtractionOutput.SerializerOptions
            .GetJsonSchemaAsNode(typeof(ExtractionOutput))
            .AsObject();

        JsonObject committed = JsonNode.Parse(ExtractionSchema.CommittedJsonText)!.AsObject();

        Assert.Equal(Names(committed["properties"]), Names(exported["properties"]));
        Assert.Equal(Required(committed), Required(exported));
        Assert.Equal(TypeOf(committed["properties"]!["actions"]), TypeOf(exported["properties"]!["actions"]));

        JsonNode committedItems = committed["properties"]!["actions"]!["items"]!;
        JsonNode exportedItems = exported["properties"]!["actions"]!["items"]!;

        Assert.Equal(Names(committedItems["properties"]), Names(exportedItems["properties"]));
        Assert.Equal(Required(committedItems), Required(exportedItems));

        foreach (string member in Names(committedItems["properties"]))
        {
            Assert.Equal(
                TypeOf(committedItems["properties"]![member]),
                TypeOf(exportedItems["properties"]![member]));
        }
    }

    [Theory]
    [InlineData("suggestedOwner", "string")]
    [InlineData("description", "string")]
    [InlineData("sourceExcerpt", "string")]
    [InlineData("confidence", "number")]
    [InlineData("suggestedDueDate", "string,null")]
    public void The_exported_member_types_are_the_committed_ones(string member, string expected)
    {
        JsonObject exported = ExtractionOutput.SerializerOptions
            .GetJsonSchemaAsNode(typeof(ExtractionOutput))
            .AsObject();

        Assert.Equal(
            expected,
            string.Join(',', TypeOf(exported["properties"]!["actions"]!["items"]!["properties"]![member])));
    }

    // --- Strict deserialization (layer one) --------------------------------------------------

    [Fact]
    public void A_schema_valid_response_deserializes()
    {
        ExtractionOutput output = Deserialize(Response(Action()));

        Assert.Single(output.Actions);
        Assert.Equal("Draft the memo", output.Actions[0].Description);
        Assert.Equal("Dana Whitfield", output.Actions[0].SuggestedOwner);
        Assert.Equal("2026-09-26", output.Actions[0].SuggestedDueDate);
        Assert.Equal(0.85, output.Actions[0].Confidence);
    }

    [Fact]
    public void An_empty_actions_array_is_a_valid_response()
    {
        Assert.Empty(Deserialize("""{"actions":[]}""").Actions);
    }

    [Fact]
    public void A_null_due_date_deserializes_as_null()
    {
        Assert.Null(Deserialize(Response(Action(dueDate: "null"))).Actions[0].SuggestedDueDate);
    }

    [Theory]
    [InlineData("""{"actions":[{"suggestedOwner":"Dana","suggestedDueDate":null,"confidence":0.5,"sourceExcerpt":"x"}]}""")]
    [InlineData("""{"actions":[{"description":"d","suggestedDueDate":null,"confidence":0.5,"sourceExcerpt":"x"}]}""")]
    [InlineData("""{"actions":[{"description":"d","suggestedOwner":"Dana","confidence":0.5,"sourceExcerpt":"x"}]}""")]
    [InlineData("""{"actions":[{"description":"d","suggestedOwner":"Dana","suggestedDueDate":null,"sourceExcerpt":"x"}]}""")]
    [InlineData("""{"actions":[{"description":"d","suggestedOwner":"Dana","suggestedDueDate":null,"confidence":0.5}]}""")]
    [InlineData("""{"notActions":[]}""")]
    public void A_missing_member_is_a_deserialization_failure(string json)
    {
        Assert.Throws<JsonException>(() => Deserialize(json));
    }

    [Fact]
    public void An_unmapped_member_is_a_deserialization_failure()
    {
        Assert.Throws<JsonException>(() => Deserialize(Response(Action(extra: ",\"priority\":\"high\""))));
    }

    [Fact]
    public void An_unmapped_member_on_the_root_is_a_deserialization_failure()
    {
        Assert.Throws<JsonException>(() => Deserialize("""{"actions":[],"model":"gpt"}"""));
    }

    /// <summary>
    /// The published schema types <c>suggestedOwner</c> as a plain string, so <c>null</c> is not a
    /// value it may take — the empty string is (<c>prd.md:154</c>).
    /// </summary>
    [Fact]
    public void A_null_owner_is_a_deserialization_failure()
    {
        Assert.Throws<JsonException>(() => Deserialize(Response(Action(owner: "null"))));
    }

    [Fact]
    public void An_empty_owner_deserializes()
    {
        Assert.Equal(string.Empty, Deserialize(Response(Action(owner: "\"\""))).Actions[0].SuggestedOwner);
    }

    [Theory]
    [InlineData("""{"actions":[{"description":1,"suggestedOwner":"D","suggestedDueDate":null,"confidence":0.5,"sourceExcerpt":"x"}]}""")]
    [InlineData("""{"actions":[{"description":"d","suggestedOwner":"D","suggestedDueDate":null,"confidence":"high","sourceExcerpt":"x"}]}""")]
    [InlineData("""{"actions":{}}""")]
    [InlineData("""{"actions":[{"description":"d","suggestedOwner":"D","suggestedDueDate":"2026-09-26","confidence":"0.5","sourceExcerpt":"x"}]}""")]
    public void A_wrongly_typed_member_is_a_deserialization_failure(string json)
    {
        Assert.Throws<JsonException>(() => Deserialize(json));
    }

    [Theory]
    [InlineData("""{"actions":[],}""")]
    [InlineData("""{"actions":[] /* done */}""")]
    public void A_trailing_comma_or_a_comment_is_a_deserialization_failure(string json)
    {
        Assert.Throws<JsonException>(() => Deserialize(json));
    }

    // --- The validator (layer two) -----------------------------------------------------------

    [Fact]
    public void A_response_inside_every_bound_passes()
    {
        Assert.Null(ExtractionOutputValidator.Validate(Output(Proposal())));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(500)]
    public void Description_lengths_on_the_boundary_pass(int length)
    {
        Assert.Null(ExtractionOutputValidator.Validate(Output(Proposal(description: new string('d', length)))));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(501)]
    public void Description_lengths_outside_the_boundary_fail(int length)
    {
        string? failure = ExtractionOutputValidator.Validate(Output(Proposal(description: new string('d', length))));

        Assert.NotNull(failure);
        Assert.Contains("actions[0].description", failure, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void Owner_lengths_on_the_boundary_pass(int length)
    {
        Assert.Null(ExtractionOutputValidator.Validate(Output(Proposal(owner: new string('o', length)))));
    }

    [Fact]
    public void An_owner_past_a_hundred_characters_fails()
    {
        string? failure = ExtractionOutputValidator.Validate(Output(Proposal(owner: new string('o', 101))));

        Assert.NotNull(failure);
        Assert.Contains("actions[0].suggestedOwner", failure, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1000)]
    public void Excerpt_lengths_on_the_boundary_pass(int length)
    {
        Assert.Null(ExtractionOutputValidator.Validate(Output(Proposal(excerpt: new string('e', length)))));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public void Excerpt_lengths_outside_the_boundary_fail(int length)
    {
        string? failure = ExtractionOutputValidator.Validate(Output(Proposal(excerpt: new string('e', length))));

        Assert.NotNull(failure);
        Assert.Contains("actions[0].sourceExcerpt", failure, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void Confidences_inside_the_range_pass(double confidence)
    {
        Assert.Null(ExtractionOutputValidator.Validate(Output(Proposal(confidence: confidence))));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.5)]
    [InlineData(double.NaN)]
    public void Confidences_outside_the_range_fail(double confidence)
    {
        string? failure = ExtractionOutputValidator.Validate(Output(Proposal(confidence: confidence)));

        Assert.NotNull(failure);
        Assert.Contains("actions[0].confidence", failure, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("2026-09-26")]
    [InlineData("2028-02-29")]
    public void Due_dates_that_are_null_or_an_ISO_date_pass(string? dueDate)
    {
        Assert.Null(ExtractionOutputValidator.Validate(Output(Proposal(dueDate: dueDate))));
    }

    [Theory]
    [InlineData("2026-13-40")]
    [InlineData("2026-02-29")]
    [InlineData("26-09-26")]
    [InlineData("2026/09/26")]
    [InlineData("next Friday")]
    [InlineData("")]
    [InlineData("2026-09-26T00:00:00Z")]
    [InlineData("2026-2-3")]
    public void Due_dates_that_are_not_an_ISO_date_fail(string dueDate)
    {
        string? failure = ExtractionOutputValidator.Validate(Output(Proposal(dueDate: dueDate)));

        Assert.NotNull(failure);
        Assert.Contains("actions[0].suggestedDueDate", failure, StringComparison.Ordinal);
    }

    /// <summary>The reason names the index, because a run's failure reason is shown to a human verbatim.</summary>
    [Fact]
    public void The_reason_names_the_index_of_the_offending_action()
    {
        string? failure = ExtractionOutputValidator.Validate(
            Output(Proposal(), Proposal(), Proposal(confidence: 2.0)));

        Assert.NotNull(failure);
        Assert.Contains("actions[2].confidence", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_response_passes_validation()
    {
        Assert.Null(ExtractionOutputValidator.Validate(Output()));
    }

    /// <summary>
    /// <c>{"actions":[null]}</c> deserializes — nullable annotations describe the C# type, they do
    /// not make System.Text.Json reject a null collection element — so the validator is what has to
    /// refuse it. Dereferencing it instead would throw out of a port documented never to throw.
    /// </summary>
    [Fact]
    public void A_null_element_in_the_actions_array_is_a_validation_failure_not_an_exception()
    {
        ExtractionOutput output = Deserialize("""{"actions":[null]}""");

        Assert.Null(output.Actions[0]);

        string? failure = ExtractionOutputValidator.Validate(output);

        Assert.NotNull(failure);
        Assert.Contains("actions[0]", failure, StringComparison.Ordinal);
        Assert.Contains("null", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void A_null_element_after_a_valid_one_is_named_by_its_own_index()
    {
        string? failure = ExtractionOutputValidator.Validate(
            Deserialize($$"""{"actions":[{{Action()}},null]}"""));

        Assert.NotNull(failure);
        Assert.Contains("actions[1]", failure, StringComparison.Ordinal);
    }

    // --- The bounds are the committed schema's (FR-5) ------------------------------------------

    /// <summary>
    /// The validator's constants and the committed schema's keywords are two statements of one
    /// contract, and the exporter parity test deliberately compares only names, <c>required</c> and
    /// types — so without this, editing <c>maxLength</c> to 800 in the committed file leaves every
    /// test green while the validator still rejects at 501.
    /// </summary>
    [Theory]
    [InlineData("description", "minLength", ExtractionOutputValidator.DescriptionMinLength)]
    [InlineData("description", "maxLength", ExtractionOutputValidator.DescriptionMaxLength)]
    [InlineData("suggestedOwner", "maxLength", ExtractionOutputValidator.SuggestedOwnerMaxLength)]
    [InlineData("sourceExcerpt", "minLength", ExtractionOutputValidator.SourceExcerptMinLength)]
    [InlineData("sourceExcerpt", "maxLength", ExtractionOutputValidator.SourceExcerptMaxLength)]
    [InlineData("confidence", "minimum", (int)ExtractionOutputValidator.ConfidenceMinimum)]
    [InlineData("confidence", "maximum", (int)ExtractionOutputValidator.ConfidenceMaximum)]
    public void The_validator_bounds_are_the_committed_schemas_bounds(string member, string keyword, int expected)
    {
        JsonNode? bound = CommittedMember(member)[keyword];

        Assert.True(bound is not null, $"The committed schema declares no {member}.{keyword}.");
        Assert.Equal(expected, bound.GetValue<double>());
    }

    /// <summary>
    /// The other half of the same contract: a keyword the committed schema declares and the
    /// validator does not enforce would be a bound nothing checks.
    /// </summary>
    [Fact]
    public void The_committed_schema_declares_no_bound_the_validator_ignores()
    {
        string[] enforced = ["type", "minLength", "maxLength", "minimum", "maximum", "format"];

        foreach (string member in Names(CommittedItems()["properties"]))
        {
            Assert.All(
                CommittedMember(member).Select(keyword => keyword.Key),
                keyword => Assert.Contains(keyword, enforced));
        }
    }

    [Fact]
    public void The_due_date_format_the_validator_enforces_is_the_one_the_schema_declares()
    {
        Assert.Equal("date", CommittedMember("suggestedDueDate")["format"]?.GetValue<string>());
        Assert.Equal("yyyy-MM-dd", ExtractionOutputValidator.DueDateFormat);
    }

    // --- The one normalizer (FR-38) ----------------------------------------------------------

    [Theory]
    [InlineData("Dana Whitfield", "dana whitfield")]
    [InlineData("DANA", "dana")]
    [InlineData("P. Ram", "p ram")]
    [InlineData("end-of-month", "endofmonth")]
    [InlineData("  padded  ", "padded")]
    [InlineData("a\t\tb", "a b")]
    [InlineData("a\n \nb", "a b")]
    [InlineData("Will it? Yes!", "will it yes")]
    [InlineData("50% + 10", "50 10")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    [InlineData(".,;:", "")]
    public void Normalization_lowercases_strips_punctuation_and_collapses_whitespace(string? text, string expected)
    {
        Assert.Equal(expected, TextNormalization.Normalize(text));
    }

    /// <summary>
    /// Stripping before collapsing is what keeps a full stop from joining two tokens. "P. Ram" is
    /// the exact string the equipment-inventory case carries, and Story 6.2 resolves it through the
    /// roster's aliases.
    /// </summary>
    [Fact]
    public void Stripping_before_collapsing_keeps_an_abbreviated_name_two_tokens()
    {
        Assert.Equal(2, TextNormalization.Normalize("P. Ram").Split(' ').Length);
    }

    [Fact]
    public void Normalization_is_idempotent()
    {
        string once = TextNormalization.Normalize("  Dana Whitfield will draft the memo.  ");

        Assert.Equal(once, TextNormalization.Normalize(once));
    }

    // --- The one verifier (FR-5) -------------------------------------------------------------

    /// <summary>
    /// The fixture bodies are hard-wrapped, so a sentence a model re-joins with a space is the same
    /// sentence the notes hold across two lines. That is the case normalizing both sides exists for.
    /// </summary>
    [Fact]
    public void An_excerpt_verifies_against_a_hard_wrapped_body()
    {
        const string Notes =
            "Dana Whitfield will confirm the loading dock booking with the building manager\nbefore the movers arrive on 2026-09-18.";

        Assert.True(ExcerptVerifier.IsSubstring(
            "Dana Whitfield will confirm the loading dock booking with the building manager before the movers arrive on 2026-09-18.",
            Notes));
    }

    [Fact]
    public void An_excerpt_verifies_through_casing_and_punctuation_drift()
    {
        Assert.True(ExcerptVerifier.IsSubstring("dana whitfield will draft the memo", "Dana Whitfield will draft the memo."));
    }

    [Fact]
    public void An_excerpt_that_is_not_in_the_notes_does_not_verify()
    {
        Assert.False(ExcerptVerifier.IsSubstring("Marcus Bell will repaint the annex.", "Dana Whitfield will draft the memo."));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".,;")]
    [InlineData(null)]
    public void An_excerpt_that_normalizes_to_nothing_does_not_verify(string? excerpt)
    {
        Assert.False(ExcerptVerifier.IsSubstring(excerpt, "Dana Whitfield will draft the memo."));
    }

    [Fact]
    public void Nothing_verifies_against_empty_notes()
    {
        Assert.False(ExcerptVerifier.IsSubstring("Dana Whitfield will draft the memo.", string.Empty));
    }

    // --- Helpers -----------------------------------------------------------------------------

    private static ExtractionOutput Deserialize(string json) =>
        JsonSerializer.Deserialize<ExtractionOutput>(json, ExtractionOutput.SerializerOptions)
        ?? throw new InvalidOperationException("Deserialization returned null.");

    private static string Response(params string[] actions) =>
        $$"""{"actions":[{{string.Join(',', actions)}}]}""";

    private static string Action(
        string owner = "\"Dana Whitfield\"",
        string dueDate = "\"2026-09-26\"",
        string extra = "") =>
        $$"""
        {"description":"Draft the memo","suggestedOwner":{{owner}},"suggestedDueDate":{{dueDate}},"confidence":0.85,"sourceExcerpt":"Dana Whitfield will draft the memo."{{extra}}}
        """;

    private static ExtractionOutput Output(params ExtractedAction[] actions) => new() { Actions = actions };

    private static ExtractedAction Proposal(
        string description = "Draft the memo",
        string owner = "Dana Whitfield",
        string? dueDate = "2026-09-26",
        double confidence = 0.85,
        string excerpt = "Dana Whitfield will draft the memo.") =>
        new()
        {
            Description = description,
            SuggestedOwner = owner,
            SuggestedDueDate = dueDate,
            Confidence = confidence,
            SourceExcerpt = excerpt,
        };

    private static JsonObject CommittedItems() =>
        JsonNode.Parse(ExtractionSchema.CommittedJsonText)!
            .AsObject()["properties"]!["actions"]!["items"]!
            .AsObject();

    private static JsonObject CommittedMember(string member) =>
        CommittedItems()["properties"]![member]!.AsObject();

    private static string[] Names(JsonNode? properties) =>
        properties is null ? [] : [.. properties.AsObject().Select(member => member.Key).Order(StringComparer.Ordinal)];

    private static string[] Required(JsonNode? schema) =>
        schema?["required"] is JsonArray required
            ? [.. required.Select(member => member!.GetValue<string>()).Order(StringComparer.Ordinal)]
            : [];

    private static string[] TypeOf(JsonNode? schema) =>
        schema?["type"] switch
        {
            JsonArray types => [.. types.Select(type => type!.GetValue<string>())],
            JsonValue type => [type.GetValue<string>()],
            _ => [],
        };
}
