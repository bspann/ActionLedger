using System.Diagnostics;
using System.Text.Json;
using ActionLedger.Application.Abstractions;
using ActionLedger.Application.Ai;
using ActionLedger.Infrastructure.Ai;
using ActionLedger.Infrastructure.Ai.Providers;
using Xunit;

namespace ActionLedger.Infrastructure.Tests;

/// <summary>
/// AD-21 — the embedded catalog and the Fake provider that answers from it. No Docker: the catalog
/// is a manifest resource of <c>ActionLedger.Infrastructure</c>, so this reads exactly the bytes a
/// container would.
/// </summary>
/// <remarks>
/// The load-bearing test here is
/// <see cref="Every_expected_excerpt_verifies_against_the_production_catalog_body"/>. Story 2.3's
/// deferred entry DW-15 records that the catalog's front-matter split was pinned only in the test
/// assembly, so a production loader that trimmed the body differently from the answer files would
/// satisfy every assertion there and still drop every proposal at runtime. That test closes it by
/// running the production verifier over the production loader's body.
/// </remarks>
public sealed class FakeProviderTests
{
    private const int CatalogCaseCount = 14;

    private const int CatalogExpectedActionCount = 31;

    private static readonly FixtureCatalog Catalog = new();

    private static readonly AiSettings Settings = new(FakeChatClientFactory.ProviderName, "v1", 90);

    // --- The catalog ---------------------------------------------------------------------------

    [Fact]
    public void All_fourteen_cases_load()
    {
        Assert.Equal(CatalogCaseCount, Catalog.Cases.Count);
    }

    /// <summary>
    /// <c>README.md</c> is excluded from the embedded glob. Dropping that <c>Exclude</c> makes this
    /// the test that goes red, because the README has no front matter and no answer file.
    /// </summary>
    [Fact]
    public void The_readme_is_not_a_case()
    {
        Assert.DoesNotContain(Catalog.Cases, fixture => fixture.Stem.Equals("README", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Every_case_carries_a_notes_body_and_an_answer_file()
    {
        foreach (FixtureCase fixture in Catalog.Cases)
        {
            Assert.NotEmpty(fixture.Notes);
            Assert.NotEmpty(fixture.ExpectedJson);
            Assert.Equal(64, fixture.NormalizedHash.Length);
        }
    }

    /// <summary>The body begins after the closing <c>---</c> line, so no case body still holds front matter.</summary>
    [Fact]
    public void No_case_body_still_holds_its_front_matter()
    {
        foreach (FixtureCase fixture in Catalog.Cases)
        {
            Assert.DoesNotContain("meetingDate:", fixture.Notes, StringComparison.Ordinal);
            Assert.DoesNotContain("attendees:", fixture.Notes, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The README's rule is byte-for-byte: no trim. The bodies open with the blank line that
    /// follows the closing <c>---</c>, and trimming it would change every normalized hash.
    /// </summary>
    [Fact]
    public void The_body_is_kept_byte_for_byte_after_the_closing_delimiter()
    {
        foreach (FixtureCase fixture in Catalog.Cases)
        {
            Assert.StartsWith("\n", fixture.Notes, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_catalog_holds_thirty_one_expected_actions()
    {
        Assert.Equal(CatalogExpectedActionCount, Catalog.Cases.Sum(fixture => Expected(fixture).Actions.Count));
    }

    /// <summary>
    /// AD-21 keys the answer table by the hash of the <em>normalized</em> notes, so a pasted copy
    /// with different wrapping and trailing whitespace still matches.
    /// </summary>
    [Fact]
    public void A_case_is_found_through_rewrapping_and_padding()
    {
        FixtureCase fixture = Case("equipment-inventory-kickoff");

        string repasted = "   " + fixture.Notes.Replace("\n", "  \r\n", StringComparison.Ordinal) + "\n\n  ";

        Assert.Equal(fixture.Stem, Catalog.Find(repasted)?.Stem);
    }

    [Fact]
    public void Notes_that_match_no_case_are_not_found()
    {
        Assert.Null(Catalog.Find("Nothing in the catalog reads like this sentence."));
    }

    /// <summary>
    /// The catalog key hashes the normalized text; <c>MeetingNotes.Sha256</c> hashes the raw text.
    /// Conflating them would make the Fake miss every re-pasted case.
    /// </summary>
    [Fact]
    public void The_catalog_key_is_not_the_hash_of_the_raw_notes()
    {
        FixtureCase fixture = Case("office-move-planning");

        Assert.NotEqual(fixture.NormalizedHash, Sha256Of(fixture.Notes));
        Assert.Equal(fixture.NormalizedHash, FixtureCatalog.NormalizedHashOf(fixture.Notes));
    }

    // --- The excerpts, through production code -------------------------------------------------

    /// <summary>
    /// Every one of the 31 committed excerpts, checked with the production
    /// <see cref="ExcerptVerifier"/> against the production <see cref="FixtureCatalog"/>'s body.
    /// The loader and the answer files agree on where the notes begin, or they do not.
    /// </summary>
    [Fact]
    public void Every_expected_excerpt_verifies_against_the_production_catalog_body()
    {
        List<string> unverified = [];

        foreach (FixtureCase fixture in Catalog.Cases)
        {
            foreach (ExtractedAction action in Expected(fixture).Actions)
            {
                if (!ExcerptVerifier.IsSubstring(action.SourceExcerpt, fixture.Notes))
                {
                    unverified.Add($"{fixture.Stem}: \"{action.SourceExcerpt}\"");
                }
            }
        }

        Assert.Empty(unverified);
    }

    [Fact]
    public void Every_answer_file_deserializes_strictly_and_passes_the_validator()
    {
        foreach (FixtureCase fixture in Catalog.Cases)
        {
            Assert.Null(ExtractionOutputValidator.Validate(Expected(fixture)));
        }
    }

    // --- The Fake, through the extractor -------------------------------------------------------

    /// <summary>
    /// Notes byte-equal to a case's body get that case's committed answer: same count, same order,
    /// same five members per action, everything kept, no warnings.
    /// </summary>
    [Fact]
    public async Task Every_case_answers_with_its_own_committed_expected_json()
    {
        foreach (FixtureCase fixture in Catalog.Cases)
        {
            ExtractionResult result = await Extractor().ExtractAsync(
                new ExtractionRequest(fixture.Notes, new DateOnly(2026, 8, 31)),
                TestContext.Current.CancellationToken);

            IReadOnlyList<ExtractedAction> expected = Expected(fixture).Actions;

            Assert.True(result.IsSucceeded, $"{fixture.Stem}: {result.FailureReason}");
            Assert.Empty(result.Dropped);
            Assert.Empty(result.Warnings);
            Assert.Equal(expected.Count, result.Kept.Count);

            for (int index = 0; index < expected.Count; index++)
            {
                Assert.Equal(expected[index].Description, result.Kept[index].Description);
                Assert.Equal(expected[index].SuggestedOwner, result.Kept[index].SuggestedOwner);
                Assert.Equal(expected[index].SuggestedDueDate, result.Kept[index].SuggestedDueDate?.ToString("yyyy-MM-dd"));
                Assert.Equal(expected[index].Confidence, result.Kept[index].Confidence);
                Assert.Equal(expected[index].SourceExcerpt, result.Kept[index].SourceExcerpt);
            }
        }
    }

    /// <summary>NFR-1 — the Fake answers within a second. It reads memory and does no I/O.</summary>
    [Fact]
    public async Task A_known_case_completes_within_one_second()
    {
        IActionExtractor extractor = Extractor();
        FixtureCase fixture = Case("office-move-planning");

        Stopwatch elapsed = Stopwatch.StartNew();

        ExtractionResult result = await extractor.ExtractAsync(
            new ExtractionRequest(fixture.Notes, new DateOnly(2026, 8, 24)),
            TestContext.Current.CancellationToken);

        elapsed.Stop();

        Assert.True(result.IsSucceeded);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(1), $"The Fake took {elapsed.ElapsedMilliseconds} ms.");
    }

    /// <summary>FR-6 — zero, never null, so Run Detail renders "0" rather than a blank.</summary>
    [Fact]
    public async Task The_metrics_are_complete_and_the_token_counts_are_zero()
    {
        ExtractionResult result = await Extractor().ExtractAsync(
            new ExtractionRequest(Case("q4-training-event").Notes, new DateOnly(2026, 8, 26)),
            TestContext.Current.CancellationToken);

        Assert.Equal(FakeChatClientFactory.ProviderName, result.Metrics.Provider);
        Assert.Equal(FakeChatClientFactory.ModelName, result.Metrics.Model);
        Assert.Equal("v1", result.Metrics.PromptVersion);
        Assert.Equal(ExtractionSchema.Version, result.Metrics.SchemaVersion);
        Assert.NotEqual(default, result.Metrics.StartedAt);
        Assert.True(result.Metrics.DurationMs >= 0);
        Assert.Equal(0, result.Metrics.InputTokens);
        Assert.Equal(0, result.Metrics.OutputTokens);
    }

    // --- The unknown-notes heuristic (PRD Glossary) ---------------------------------------------

    [Fact]
    public async Task Unknown_notes_give_one_proposal_per_modal_verb_sentence_in_order()
    {
        const string Notes =
            "Dana Whitfield will draft the memo.\nPriya Ramaswamy should book the room.\n"
            + "Marcus Bell needs to call the vendor.\nDana Whitfield must sign the form.\n"
            + "Priya Ramaswamy will file the report.\nMarcus Bell will never be reached, because this is a sixth sentence.";

        ExtractionResult result = await Extractor().ExtractAsync(
            new ExtractionRequest(Notes, new DateOnly(2026, 8, 31)),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSucceeded);
        Assert.Equal(5, result.Kept.Count);
        Assert.Empty(result.Dropped);

        Assert.Equal(
            [
                "Dana Whitfield will draft the memo.",
                "Priya Ramaswamy should book the room.",
                "Marcus Bell needs to call the vendor.",
                "Dana Whitfield must sign the form.",
                "Priya Ramaswamy will file the report.",
            ],
            result.Kept.Select(proposal => proposal.SourceExcerpt));

        Assert.Equal([0.85, 0.85, 0.85, 0.85, 0.55], result.Kept.Select(proposal => proposal.Confidence));
        Assert.All(result.Kept, proposal => Assert.Null(proposal.SuggestedDueDate));
        Assert.All(result.Kept, proposal => Assert.Equal(proposal.SourceExcerpt, proposal.Description));
    }

    [Fact]
    public async Task Unknown_notes_take_the_first_capitalized_name_as_the_owner()
    {
        ExtractionResult result = await Extractor().ExtractAsync(
            new ExtractionRequest("Dana Whitfield will draft the memo.", new DateOnly(2026, 8, 31)),
            TestContext.Current.CancellationToken);

        Assert.Equal("Dana Whitfield", Assert.Single(result.Kept).SuggestedOwner);
    }

    /// <summary>
    /// Every sentence starts with a capital, so a sequence that is nothing but the sentence's first
    /// word names nobody.
    /// </summary>
    [Fact]
    public async Task Unknown_notes_with_no_name_leave_the_owner_empty()
    {
        ExtractionResult result = await Extractor().ExtractAsync(
            new ExtractionRequest("The badge printer must be replaced.", new DateOnly(2026, 8, 31)),
            TestContext.Current.CancellationToken);

        Assert.Equal(string.Empty, Assert.Single(result.Kept).SuggestedOwner);
    }

    [Fact]
    public async Task Notes_with_no_modal_verb_succeed_with_no_proposals_and_no_warnings()
    {
        ExtractionResult result = await Extractor().ExtractAsync(
            new ExtractionRequest("The coffee was cold. Nobody liked it, and the meeting ended early.", new DateOnly(2026, 8, 31)),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSucceeded);
        Assert.Empty(result.Kept);
        Assert.Empty(result.Dropped);
        Assert.Empty(result.Warnings);
        Assert.Null(result.FailureReason);
    }

    /// <summary>A modal verb inside a longer word is not a modal verb.</summary>
    [Theory]
    [InlineData("The goodwill of the office was noted.")]
    [InlineData("Mustard was discussed at length.")]
    [InlineData("The shoulder of the road is unpaved.")]
    public async Task A_modal_verb_is_matched_as_a_whole_word(string notes)
    {
        ExtractionResult result = await Extractor().ExtractAsync(
            new ExtractionRequest(notes, new DateOnly(2026, 8, 31)),
            TestContext.Current.CancellationToken);

        Assert.Empty(result.Kept);
    }

    /// <summary>
    /// Pasted notes are mostly lists, and a bullet list rarely punctuates its items. Matching
    /// across newlines made this one proposal quoting the whole block; a line break ends a
    /// sentence, so it is three.
    /// </summary>
    [Fact]
    public async Task An_unpunctuated_bullet_list_is_one_proposal_per_bullet()
    {
        const string Notes =
            "Actions agreed:\n- Dana will book the room\n- Priya will order the chairs\n- Marcus will confirm the caterer";

        ExtractionResult result = await Extractor().ExtractAsync(
            new ExtractionRequest(Notes, new DateOnly(2026, 8, 31)),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSucceeded);
        Assert.Equal(
            [
                "- Dana will book the room",
                "- Priya will order the chairs",
                "- Marcus will confirm the caterer",
            ],
            result.Kept.Select(proposal => proposal.SourceExcerpt));
    }

    [Fact]
    public async Task A_line_break_ends_a_sentence_even_without_a_blank_line()
    {
        ExtractionResult result = await Extractor().ExtractAsync(
            new ExtractionRequest("Dana will book the room\nPriya will order the chairs", new DateOnly(2026, 8, 31)),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Kept.Count);
    }

    /// <summary>
    /// Between the description bound (500) and the excerpt bound (1,000): the excerpt is the
    /// sentence whole, and only the description is shortened. The run succeeds, which is the point
    /// — the Fake must never emit output its own validator rejects.
    /// </summary>
    [Fact]
    public async Task A_sentence_past_the_description_bound_still_yields_a_proposal()
    {
        string sentence = "Dana Whitfield will " + new string('x', 580) + " done.";

        ExtractionResult result = await Extractor().ExtractAsync(
            new ExtractionRequest(sentence, new DateOnly(2026, 8, 31)),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSucceeded, result.FailureReason);

        ExtractedProposal proposal = Assert.Single(result.Kept);

        Assert.Equal(sentence, proposal.SourceExcerpt);
        Assert.Equal(500, proposal.Description.Length);
        Assert.StartsWith(proposal.Description, sentence, StringComparison.Ordinal);
    }

    /// <summary>
    /// Past the excerpt bound there is nothing honest to return: an excerpt is only an excerpt if
    /// it is verbatim, and FR-5 makes over-length a failure rather than a trim. The sentence is
    /// skipped and the run still succeeds.
    /// </summary>
    [Fact]
    public async Task A_sentence_past_the_excerpt_bound_is_skipped_and_the_run_still_succeeds()
    {
        ExtractionResult result = await Extractor().ExtractAsync(
            new ExtractionRequest("Dana Whitfield will " + new string('x', 1180) + " done.", new DateOnly(2026, 8, 31)),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSucceeded, result.FailureReason);
        Assert.Empty(result.Kept);
    }

    /// <summary>
    /// A determiner opens a noun phrase, never a name. "The IT" on the Review Screen costs a
    /// reviewer more than the empty string does.
    /// </summary>
    [Theory]
    [InlineData("The IT team must patch the server.")]
    [InlineData("Our Facilities group will repaint the annex.")]
    [InlineData("These Vendor Partners should send a quote.")]
    [InlineData("Every Facilities lead must sign the register.")]
    [InlineData("All Vendor Partners should send a quote.")]
    [InlineData("His Team will follow up on the invoice.")]
    [InlineData("(Our Payroll group must file the return.)")]
    public async Task A_capitalized_run_opening_with_a_determiner_is_not_an_owner(string notes)
    {
        ExtractionResult result = await Extractor().ExtractAsync(
            new ExtractionRequest(notes, new DateOnly(2026, 8, 31)),
            TestContext.Current.CancellationToken);

        Assert.Equal(string.Empty, Assert.Single(result.Kept).SuggestedOwner);
    }

    /// <summary>
    /// The documented cost of the leading-word rule, and the reason it stops rather than scans on:
    /// "Marcus" is indistinguishable from "The", and the next capitalized run is the object.
    /// </summary>
    [Theory]
    [InlineData("Marcus needs to call the vendor.", "")]
    [InlineData("Dana will email Marcus Chen.", "")]
    public async Task A_single_word_leading_name_yields_no_owner_and_ends_the_search(string notes, string expected)
    {
        ExtractionResult result = await Extractor().ExtractAsync(
            new ExtractionRequest(notes, new DateOnly(2026, 8, 31)),
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, Assert.Single(result.Kept).SuggestedOwner);
    }

    /// <summary>
    /// The owner bound is the only thing keeping an all-caps line — which pasted notes routinely
    /// carry — from making the Fake emit output its own validator rejects, failing the whole run.
    /// </summary>
    [Fact]
    public async Task A_capitalized_run_past_the_owner_bound_leaves_the_owner_empty()
    {
        // No determiner opens it, so the run reaches the length bound rather than being skipped as
        // a noun phrase — which is what makes this the bound's own test and not the determiner's.
        const string notes =
            "REMOTE AND ONSITE STAFF MUST COMPLETE THE ANNUAL INFORMATION SECURITY COMPLIANCE TRAINING MODULE BEFORE QUARTER END.";

        ExtractionResult result = await Extractor().ExtractAsync(
            new ExtractionRequest(notes, new DateOnly(2026, 8, 31)),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSucceeded, result.FailureReason);
        Assert.Equal(string.Empty, Assert.Single(result.Kept).SuggestedOwner);
    }

    /// <summary>
    /// The description is cut on a rune boundary, so an astral character straddling the bound does
    /// not leave a lone surrogate in a string headed for a database column and a browser.
    /// </summary>
    [Fact]
    public async Task A_description_cut_at_the_bound_never_ends_in_a_lone_surrogate()
    {
        // 499 ASCII characters, then an emoji: its high surrogate sits at index 499, so an
        // unguarded sentence[..500] would keep the high half and drop the low one.
        string sentence = $"{new string('x', 499)}\U0001F600 and the rest of a long sentence that runs well past the bound will be cut.";

        ExtractionResult result = await Extractor().ExtractAsync(
            new ExtractionRequest(sentence, new DateOnly(2026, 8, 31)),
            TestContext.Current.CancellationToken);

        string description = Assert.Single(result.Kept).Description;

        Assert.Equal(499, description.Length);
        Assert.False(char.IsSurrogate(description[^1]), "The cut left a lone surrogate in the description.");
    }

    /// <summary>
    /// A full stop inside a number or an abbreviation is not the end of a sentence. Splitting on it
    /// cut the excerpt after "2.", and the fragment still verified — a genuine substring of the
    /// notes — so a truncated half-sentence reached the Review Screen looking like a real citation.
    /// </summary>
    [Theory]
    [InlineData("Dana Whitfield will order 2.5 tons of salt.", "Dana Whitfield will order 2.5 tons of salt.")]
    [InlineData("Dana Whitfield will email ops@example.com today.", "Dana Whitfield will email ops@example.com today.")]
    public async Task An_interior_full_stop_does_not_end_a_sentence(string notes, string expected)
    {
        ExtractionResult result = await Extractor().ExtractAsync(
            new ExtractionRequest(notes, new DateOnly(2026, 8, 31)),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSucceeded, result.FailureReason);
        Assert.Equal(expected, Assert.Single(result.Kept).SourceExcerpt);
    }

    [Fact]
    public async Task The_fake_is_deterministic()
    {
        ExtractionRequest request = new("Dana Whitfield will draft the memo.", new DateOnly(2026, 8, 31));

        ExtractionResult first = await Extractor().ExtractAsync(request, TestContext.Current.CancellationToken);
        ExtractionResult second = await Extractor().ExtractAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(first.Kept, second.Kept);
    }

    // --- Helpers ---------------------------------------------------------------------------------

    private static IActionExtractor Extractor()
    {
        FakeChatClientFactory factory = new(Catalog);

        return new ChatClientActionExtractor(
            factory.Create(),
            new PromptCatalog(Settings),
            factory,
            Settings,
            TimeProvider.System);
    }

    private static FixtureCase Case(string stem) =>
        Catalog.Cases.SingleOrDefault(fixture => fixture.Stem == stem)
        ?? throw new InvalidOperationException($"The catalog holds no case '{stem}'.");

    private static ExtractionOutput Expected(FixtureCase fixture) =>
        JsonSerializer.Deserialize<ExtractionOutput>(fixture.ExpectedJson, ExtractionOutput.SerializerOptions)
        ?? throw new InvalidOperationException($"{fixture.Stem}.expected.json is not an extraction document.");

    private static string Sha256Of(string text) =>
        Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
}
