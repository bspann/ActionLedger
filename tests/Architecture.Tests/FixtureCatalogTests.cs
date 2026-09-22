using ActionLedger.Infrastructure.Ai;
using ActionLedger.Infrastructure.Seed;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Xunit;

namespace ActionLedger.Architecture.Tests;

/// <summary>
/// AD-21 — <c>fixtures/extraction/</c>, the one catalog the Fake provider, the demo seeder and the
/// Evaluation Gate all read, pinned as text.
///
/// None of this is observable from an assembly. No type changes when a <c>sourceExcerpt</c> drifts
/// one word from the sentence it claims to quote, when a fourth case is marked <c>seed: true</c>,
/// when the injected span loses the property that no legitimate action overlaps it, or when an
/// expected owner stops resolving through <c>roster.json</c> — and every one of those produces a
/// catalog that loads and then scores wrongly. Story 2.4's excerpt verifier silently drops a
/// proposal whose excerpt it cannot find, which the Gate charges against a Fake provider that must
/// score 1.0, so a broken excerpt surfaces as an unexplained quality regression three stories
/// later. Without these assertions AD-21 has no enforcement at all.
///
/// The assertions pin literal text and parse the front matter with a regex rather than taking a
/// YAML dependency, for the reason <c>ComposeTopologyTests</c> already states: the spine's Stack
/// table is the package allowlist, and adding a YAML package for one test is a bigger deviation
/// than a regex. The expected files are read with <c>System.Text.Json</c>, which is in the
/// framework; <c>JsonSchema.Net</c> is licence-banned (<c>Directory.Packages.props</c>), so the
/// FR5 shape is asserted member by member here.
///
/// Excerpts are compared by ordinal <c>string.Contains</c>. That is strictly stronger than the
/// normalized comparison Story 2.4's verifier applies, and it keeps this assembly clear of the
/// second normalizer <c>DependencyRuleTests</c> Rule 5 forbids.
/// </summary>
public sealed class FixtureCatalogTests
{
    private const string CatalogFolder = "fixtures/extraction";
    private const string RosterFile = "roster.json";
    private const string ReadmeFile = "README.md";
    private const string NotesExtension = ".md";
    private const string ExpectedSuffix = ".expected.json";

    /// <summary>The five members FR5 requires on every action, with no sixth and none missing.</summary>
    private static readonly string[] TheFiveSchemaMembers =
        ["description", "suggestedOwner", "suggestedDueDate", "confidence", "sourceExcerpt"];

    /// <summary>
    /// The three people the seeder creates, read off <c>DemoDataSeeder.DemoUsers</c> rather than
    /// retyped. A hand-copied list lets the roster and this test agree with each other while both
    /// disagree with the seeder, which is the one disagreement that breaks Story 6.1.
    /// </summary>
    private static readonly string[] TheRosterDisplayNames =
        [.. DemoDataSeeder.DemoUsers.Select(user => user.DisplayName)];

    /// <summary>
    /// The PRD addendum's aliases for Priya, plus <c>P. Ram</c>, the exact string the 0.55
    /// equipment-inventory proposal carries and the one Story 6.2 must resolve to score it.
    /// </summary>
    private static readonly string[] ThePriyaAliases = ["Priya", "P. Ramaswamy", "Priya R.", "P. Ram"];

    /// <summary>Credentials live in <c>DemoDataSeeder</c> and are not duplicated in the roster.</summary>
    private static readonly string[] TheCredentialWords = ["username", "password", "passwordHash"];

    /// <summary>The two members Story 6.2's injection hard fail inspects on every action.</summary>
    private static readonly string[] TheInjectionCheckedMembers = ["description", "sourceExcerpt"];

    /// <summary>
    /// Expected actions per case, and 31 in total. The total is the number the deferred threshold
    /// conversation is about: the PRD addendum derives the 0.80 recall gate from "roughly 50 to 70
    /// expected actions", and at 31 the same gate tolerates six misses rather than ten to fourteen.
    /// Story 6.2 authors <c>thresholds.json</c> against this count, so it cannot be free to drift —
    /// a case quietly gaining or losing a commitment moves the recall arithmetic under that story.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, int> TheExpectedActionCounts =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["badge-printer-replacement"] = 2,
            ["break-room-refresh"] = 2,
            ["coffee-service-discussion"] = 0,
            ["equipment-inventory-kickoff"] = 1,
            ["intern-orientation-packet"] = 2,
            ["loading-dock-scheduling"] = 3,
            ["office-move-planning"] = 6,
            ["parking-lot-repaving-debate"] = 0,
            ["q4-training-event"] = 5,
            ["quarterly-safety-walkthrough"] = 2,
            ["records-shredding-vendor"] = 2,
            ["server-room-air-conditioning"] = 2,
            ["vendor-onboarding-notes"] = 2,
            ["visitor-parking-signage"] = 2,
        };

    /// <summary>
    /// The case table. The epic fixes the spread (4 + 2 + 2 + 2 + 2 + 1 + 1) and the three seed
    /// subjects; this dictionary is where that spread is written down, because the front matter
    /// AD-21 specifies carries no category key and none may be added.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, CaseType> TheCaseTable =
        new Dictionary<string, CaseType>(StringComparer.Ordinal)
        {
            ["office-move-planning"] = CaseType.PlainOwnerAndDate,
            ["q4-training-event"] = CaseType.PlainOwnerAndDate,
            ["loading-dock-scheduling"] = CaseType.PlainOwnerAndDate,
            ["badge-printer-replacement"] = CaseType.PlainOwnerAndDate,
            ["break-room-refresh"] = CaseType.NoOwner,
            ["visitor-parking-signage"] = CaseType.NoOwner,
            ["records-shredding-vendor"] = CaseType.NoDueDate,
            ["intern-orientation-packet"] = CaseType.NoDueDate,
            ["equipment-inventory-kickoff"] = CaseType.RelativeDate,
            ["quarterly-safety-walkthrough"] = CaseType.RelativeDate,
            ["coffee-service-discussion"] = CaseType.DiscussionOnly,
            ["parking-lot-repaving-debate"] = CaseType.DiscussionOnly,
            ["server-room-air-conditioning"] = CaseType.DuplicateMention,
            ["vendor-onboarding-notes"] = CaseType.Injection,
        };

    /// <summary>The epic's case-type counts, which the catalog's spread must reproduce exactly.</summary>
    private static readonly IReadOnlyDictionary<CaseType, int> TheEpicsSpread =
        new Dictionary<CaseType, int>
        {
            [CaseType.PlainOwnerAndDate] = 4,
            [CaseType.NoOwner] = 2,
            [CaseType.NoDueDate] = 2,
            [CaseType.RelativeDate] = 2,
            [CaseType.DiscussionOnly] = 2,
            [CaseType.DuplicateMention] = 1,
            [CaseType.Injection] = 1,
        };

    /// <summary>
    /// The commitment <c>server-room-air-conditioning</c> states twice. The notes say it in two
    /// sentences; the answer file may claim it once, because two entries would collapse under the
    /// Gate's greedy one-to-one matcher and score as a false positive.
    /// </summary>
    private const string TheDuplicatedCommitment =
        "will schedule the air conditioning service call by 2026-06-12";

    private static readonly IReadOnlyList<FixtureCase> Catalog = LoadCatalog();

    /// <summary>
    /// The embedded catalog as <c>ActionLedger.Infrastructure</c> loads it, so the two independent
    /// implementations of the front-matter rule can be compared against each other.
    /// </summary>
    private static readonly FixtureCatalog ProductionCatalog = new();

    /// <summary>Every case stem, for the per-case theories.</summary>
    public static TheoryData<string> TheCaseStems
    {
        get
        {
            TheoryData<string> stems = new();

            foreach (string stem in TheCaseTable.Keys.OrderBy(stem => stem, StringComparer.Ordinal))
            {
                stems.Add(stem);
            }

            return stems;
        }
    }

    // --- the split this file and production both perform ---------------------------------------

    /// <summary>
    /// The production loader's notes body, per case, byte-equal to this file's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is deferred entry DW-15's actual cause, not its symptom. The front-matter rule is
    /// written twice — <see cref="SplitFrontMatter"/> here and <c>FixtureCatalog</c>'s own regex in
    /// <c>ActionLedger.Infrastructure</c> — and prose in <c>README.md</c> was the only thing
    /// holding them together. Every assertion in this file reads a body this file computed, so a
    /// production loader that trimmed, re-wrapped or cut at a different offset satisfied all of
    /// them and still disagreed at runtime.
    /// </para>
    /// <para>
    /// Byte equality, not excerpt verification: every committed excerpt is an interior sentence, so
    /// a trim cannot break one. What a trim does break is the normalized hash the Fake provider
    /// keys on, and only comparing the bodies themselves catches that.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(TheCaseStems))]
    public void The_production_loader_splits_the_body_exactly_as_this_file_does(string stem)
    {
        FixtureCase expected = Case(stem);

        ActionLedger.Infrastructure.Ai.FixtureCase produced =
            ProductionCatalog.Cases.SingleOrDefault(fixture => string.Equals(fixture.Stem, stem, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"The production FixtureCatalog holds no case '{stem}'.");

        Assert.Equal(expected.Body, produced.Notes);
    }

    [Fact]
    public void The_production_loader_holds_exactly_the_cases_this_file_reads()
    {
        Assert.Equal(
            Catalog.Select(fixture => fixture.Stem).Order(StringComparer.Ordinal),
            ProductionCatalog.Cases.Select(fixture => fixture.Stem).Order(StringComparer.Ordinal));
    }

    // --- the folder ---------------------------------------------------------------------------

    [Fact]
    public void Catalog_holds_fourteen_paired_cases_a_roster_and_a_readme()
    {
        DirectoryInfo folder = CatalogDirectory();

        HashSet<string> expected = new(StringComparer.Ordinal) { RosterFile, ReadmeFile };

        foreach (string stem in TheCaseTable.Keys)
        {
            expected.Add($"{stem}{NotesExtension}");
            expected.Add($"{stem}{ExpectedSuffix}");
        }

        HashSet<string> actual = new(VisibleFiles(folder).Select(file => file.Name), StringComparer.Ordinal);

        Assert.Empty(folder.GetDirectories());

        Assert.True(
            actual.SetEquals(expected),
            $"""
             {CatalogFolder} does not hold exactly the AD-21 catalog.
             Missing: {Join(expected.Except(actual))}
             Unexpected: {Join(actual.Except(expected))}
             """);
    }

    [Theory]
    [MemberData(nameof(TheCaseStems))]
    public void Every_notes_file_has_a_sibling_expected_file(string stem)
    {
        DirectoryInfo folder = CatalogDirectory();

        Assert.True(
            File.Exists(Path.Combine(folder.FullName, $"{stem}{NotesExtension}")),
            $"Expected {stem}{NotesExtension} in {CatalogFolder}.");

        Assert.True(
            File.Exists(Path.Combine(folder.FullName, $"{stem}{ExpectedSuffix}")),
            $"Expected {stem}{ExpectedSuffix} beside {stem}{NotesExtension}. AD-21 pairs every case with its answer.");
    }

    // --- front matter -------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(TheCaseStems))]
    public void Every_case_declares_the_front_matter_AD21_specifies(string stem)
    {
        FixtureCase fixture = Case(stem);

        Assert.False(
            string.IsNullOrWhiteSpace(fixture.Title),
            $"{stem}{NotesExtension} declares no title. AD-21 requires one.");

        Assert.True(
            IsIsoDate(fixture.MeetingDate),
            $"{stem}{NotesExtension} declares meetingDate '{fixture.MeetingDate}'. AD-21 requires YYYY-MM-DD.");

        Assert.NotEmpty(fixture.Attendees);

        Assert.True(
            fixture.Seed is "true" or "false",
            $"{stem}{NotesExtension} declares seed '{fixture.Seed}'. AD-21 requires true or false.");

        Assert.False(
            string.IsNullOrWhiteSpace(fixture.Body),
            $"{stem}{NotesExtension} has an empty notes body. Every excerpt is quoted out of it.");
    }

    [Fact]
    public void Exactly_three_cases_are_seed_cases()
    {
        string[] seeded = [.. Catalog.Where(fixture => fixture.Seed == "true").Select(fixture => fixture.Stem)];

        Assert.True(
            seeded.Length == 3,
            $"Expected exactly three seed:true cases for Story 6.1, found {seeded.Length}: {Join(seeded)}.");
    }

    [Fact]
    public void Exactly_one_case_declares_an_injection_span()
    {
        string[] injected = [.. Catalog.Where(fixture => fixture.InjectionSpan is not null).Select(fixture => fixture.Stem)];

        Assert.True(
            injected.Length == 1,
            $"FR8 wants exactly one case carrying injectionSpan, found {injected.Length}: {Join(injected)}.");

        Assert.Equal("vendor-onboarding-notes", injected[0]);
    }

    [Fact]
    public void Every_title_and_meeting_date_pair_is_unique()
    {
        string[] duplicates =
        [
            .. Catalog
                .GroupBy(fixture => $"{fixture.Title}|{fixture.MeetingDate}", StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key),
        ];

        Assert.True(
            duplicates.Length == 0,
            $"AD-20 indexes meeting(title, meeting_date), so a seeded pair may not repeat: {Join(duplicates)}.");
    }

    // --- the seed storyline -------------------------------------------------------------------

    /// <summary>
    /// Story 6.1 creates every seeded Meeting on <c>FixedClock.SeedInstant</c>, so a seed case
    /// dated after it would be a Meeting created before it happened. The instant is read off the
    /// type rather than retyped, so moving it moves this assertion with it.
    /// </summary>
    [Fact]
    public void Every_seed_case_is_dated_on_or_before_the_seed_instant()
    {
        DateOnly seedInstant = DateOnly.FromDateTime(FixedClock.SeedInstant.UtcDateTime);

        foreach (FixtureCase fixture in Catalog.Where(fixture => fixture.Seed == "true"))
        {
            DateOnly meetingDate = MeetingDate(fixture.Stem);

            Assert.True(
                meetingDate <= seedInstant,
                $"{fixture.Stem}{NotesExtension} is dated {Iso(meetingDate)}, after FixedClock.SeedInstant ({Iso(seedInstant)}); Story 6.1 would seed a Meeting dated in its own future.");
        }
    }

    [Fact]
    public void The_three_seed_cases_are_the_storyline_subjects()
    {
        string[] titles =
        [
            .. Catalog
                .Where(fixture => fixture.Seed == "true")
                .Select(fixture => fixture.Title ?? string.Empty)
                .OrderBy(title => title, StringComparer.Ordinal),
        ];

        string[] storyline = ["Equipment inventory kickoff", "Office move planning", "Q4 training event"];

        Assert.Equal(storyline, titles);
    }

    [Fact]
    public void The_office_move_seed_case_carries_the_proposal_Dana_edits()
    {
        IReadOnlyList<JsonObject> actions = Actions("office-move-planning");

        Assert.True(
            actions.Count == 6,
            $"The PRD addendum's Meeting 1 reviews six proposals, found {actions.Count}.");

        // Exactly one: a second match would leave Story 6.1's edit target ambiguous.
        Assert.Single(
            actions,
            action => Text(action, "suggestedDueDate") == "2026-09-26"
                && Text(action, "suggestedOwner") == "Dana Whitfield");
    }

    [Fact]
    public void The_training_event_seed_case_carries_five_proposals_one_low_confidence()
    {
        IReadOnlyList<JsonObject> actions = Actions("q4-training-event");

        Assert.True(
            actions.Count == 5,
            $"The PRD addendum's Meeting 2 shows five Pending proposals, found {actions.Count}.");

        // Read from the application's own configuration rather than retyped: a threshold move that
        // left this literal behind would leave the seeded Meeting 2 with nothing for the Review
        // Screen to flag, which is exactly what step 2 of the demo click path puts on stage.
        double threshold = ConfiguredLowConfidenceThreshold();

        Assert.True(
            actions.Count(action => Number(action, "confidence") < threshold) == 1,
            $"The PRD addendum's Meeting 2 shows exactly one proposal below the configured Low Confidence Threshold ({threshold.ToString(CultureInfo.InvariantCulture)}).");
    }

    [Fact]
    public void The_equipment_inventory_seed_case_carries_the_audit_showcase_proposal()
    {
        JsonObject action = Assert.Single(Actions("equipment-inventory-kickoff"));

        Assert.Equal(0.55, Number(action, "confidence"), 10);
        Assert.Equal("P. Ram", Text(action, "suggestedOwner"));
    }

    // --- the FR5 shape ------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(TheCaseStems))]
    public void Every_expected_file_is_an_object_whose_only_member_is_actions(string stem)
    {
        string[] members = [.. Case(stem).Expected.Select(member => member.Key)];

        Assert.True(
            members.Length == 1 && string.Equals(members[0], "actions", StringComparison.Ordinal),
            $"{stem}{ExpectedSuffix} declares [{Join(members)}]; the schema's root has the single member 'actions'.");

        Assert.Equal(
            JsonValueKind.Array,
            Case(stem).Expected["actions"]?.GetValueKind() ?? JsonValueKind.Null);
    }

    [Theory]
    [MemberData(nameof(TheCaseStems))]
    public void Every_expected_action_declares_all_five_schema_members(string stem)
    {
        string[] required = [.. TheFiveSchemaMembers.OrderBy(member => member, StringComparer.Ordinal)];
        int index = 0;

        foreach (JsonObject action in Actions(stem))
        {
            string[] members = [.. action.Select(member => member.Key).OrderBy(member => member, StringComparer.Ordinal)];

            Assert.True(
                members.SequenceEqual(required, StringComparer.Ordinal),
                $"{stem}{ExpectedSuffix} action {index} declares [{Join(members)}]; FR5 requires exactly [{Join(required)}], with an empty string for an absent owner and explicit null for an absent due date.");

            index++;
        }
    }

    [Theory]
    [MemberData(nameof(TheCaseStems))]
    public void Every_expected_action_respects_the_FR5_ranges(string stem)
    {
        int index = 0;

        foreach (JsonObject action in Actions(stem))
        {
            string where = $"{stem}{ExpectedSuffix} action {index}";

            string description = RequiredText(action, "description", where);
            Assert.True(
                description.Length is >= 1 and <= 500,
                $"{where}: description is {description.Length} characters; FR5 allows 1 to 500.");

            string excerpt = RequiredText(action, "sourceExcerpt", where);
            Assert.True(
                excerpt.Length is >= 1 and <= 1000,
                $"{where}: sourceExcerpt is {excerpt.Length} characters; FR5 allows 1 to 1,000.");

            JsonValueKind ownerKind = Kind(action, "suggestedOwner");
            Assert.True(
                ownerKind is JsonValueKind.String,
                $"{where}: suggestedOwner is {ownerKind}; the published schema types it as a plain string, never null — an unknown owner is the empty string.");

            string owner = Text(action, "suggestedOwner")!;
            Assert.True(
                owner.Length <= 100,
                $"{where}: suggestedOwner is {owner.Length} characters; FR5 allows up to 100.");

            JsonValueKind dueDateKind = Kind(action, "suggestedDueDate");
            Assert.True(
                dueDateKind is JsonValueKind.Null or JsonValueKind.String,
                $"{where}: suggestedDueDate is {dueDateKind}; FR5 allows an ISO date or null.");

            if (dueDateKind is JsonValueKind.String)
            {
                Assert.True(
                    IsIsoDate(Text(action, "suggestedDueDate")),
                    $"{where}: suggestedDueDate is '{Text(action, "suggestedDueDate")}'; FR5 requires YYYY-MM-DD.");
            }

            Assert.True(
                Kind(action, "confidence") is JsonValueKind.Number,
                $"{where}: confidence is {Kind(action, "confidence")}; FR5 requires a number.");

            double confidence = Number(action, "confidence");
            Assert.True(
                confidence is >= 0 and <= 1,
                $"{where}: confidence is {confidence.ToString(CultureInfo.InvariantCulture)}; FR5 allows 0 to 1.");

            index++;
        }
    }

    /// <summary>
    /// A commitment cannot be due before the meeting that made it. Such an answer would seed an
    /// action that is Overdue the moment it is created, for no reason the notes give.
    /// </summary>
    [Theory]
    [MemberData(nameof(TheCaseStems))]
    public void No_expected_due_date_precedes_its_meeting_date(string stem)
    {
        DateOnly meetingDate = MeetingDate(stem);
        int index = 0;

        foreach (JsonObject action in Actions(stem))
        {
            if (Text(action, "suggestedDueDate") is { } dueDate)
            {
                Assert.True(
                    DateOnly.ParseExact(dueDate, "yyyy-MM-dd", CultureInfo.InvariantCulture) >= meetingDate,
                    $"{stem}{ExpectedSuffix} action {index} is due {dueDate}, before its meetingDate {Iso(meetingDate)}.");
            }

            index++;
        }
    }

    [Theory]
    [MemberData(nameof(TheCaseStems))]
    public void Every_source_excerpt_is_a_verbatim_substring_of_its_notes_body(string stem)
    {
        FixtureCase fixture = Case(stem);
        int index = 0;

        foreach (JsonObject action in Actions(stem))
        {
            string excerpt = RequiredText(action, "sourceExcerpt", $"{stem}{ExpectedSuffix} action {index}");

            Assert.True(
                fixture.Body.Contains(excerpt, StringComparison.Ordinal),
                $"""
                 {stem}{ExpectedSuffix} action {index} quotes a sentence that is not in {stem}{NotesExtension}.
                 Excerpts are copied out of the notes, never retyped: Story 2.4's verifier drops a
                 proposal it cannot find, and the Gate charges the miss against a Fake that must score 1.0.
                 Excerpt: {excerpt}
                 """);

            index++;
        }
    }

    /// <summary>
    /// An excerpt is the evidence for the action that quotes it, so the other members have to agree
    /// with it. Without this the answer file can name an owner the sentence never mentions, or a due
    /// date the sentence contradicts, and stay green — and a real provider returning what the notes
    /// actually say would then be scored a miss by Story 6.2.
    /// </summary>
    [Theory]
    [MemberData(nameof(TheCaseStems))]
    public void Every_action_agrees_with_the_sentence_it_quotes(string stem)
    {
        int index = 0;

        foreach (JsonObject action in Actions(stem))
        {
            string where = $"{stem}{ExpectedSuffix} action {index}";
            string excerpt = RequiredText(action, "sourceExcerpt", where);
            string owner = RequiredText(action, "suggestedOwner", where);

            if (owner.Length > 0)
            {
                Assert.True(
                    excerpt.Contains(owner, StringComparison.OrdinalIgnoreCase),
                    $"{where} names owner '{owner}', who does not appear in the sentence it quotes: {excerpt}");
            }

            // The three relative-date answers carry no literal date in their sentence on purpose;
            // their arithmetic is pinned by the relative-date test instead.
            string[] spelled = [.. Regex.Matches(excerpt, @"\d{4}-\d{2}-\d{2}").Select(match => match.Value)];

            if (spelled.Length > 0 && Text(action, "suggestedDueDate") is { } dueDate)
            {
                Assert.True(
                    spelled.Contains(dueDate, StringComparer.Ordinal),
                    $"{where} is due {dueDate}, which is not a date its sentence spells ({Join(spelled)}): {excerpt}");
            }

            index++;
        }
    }

    [Theory]
    [MemberData(nameof(TheCaseStems))]
    public void Every_case_expects_the_number_of_actions_the_catalog_is_costed_at(string stem)
    {
        Assert.True(
            TheExpectedActionCounts.TryGetValue(stem, out int expected),
            $"{stem} has no entry in the expected-action count table. A new case changes the recall arithmetic Story 6.2 reads.");

        Assert.True(
            Actions(stem).Count == expected,
            $"{stem}{ExpectedSuffix} holds {Actions(stem).Count} actions, expected {expected}.");
    }

    [Fact]
    public void The_catalog_holds_thirty_one_expected_actions()
    {
        int total = Catalog.Sum(fixture => Actions(fixture.Stem).Count);

        Assert.True(
            total == 31,
            $"""
             The catalog holds {total} expected actions, not 31.
             That number is what the deferred recall-threshold conversation is about: the PRD
             addendum derives the 0.80 gate from "roughly 50 to 70 expected actions", and at 31 the
             same gate tolerates six misses. Story 6.2 authors thresholds.json against this count,
             so move it deliberately and restate the rationale, never as a side effect.
             """);
    }

    /// <summary>
    /// The notes are promised to Story 2.4 byte for byte, and <c>.gitattributes</c> pins
    /// <c>fixtures/**</c> and <c>prompts/**</c> to LF so a CRLF checkout on the Windows host FR36
    /// requires cannot change them. Nothing observed that promise: the front-matter regexes are
    /// deliberately CRLF-tolerant and every excerpt sits on one line, so the suite stayed green with
    /// the whole catalog converted to CRLF. This is the assertion that makes the attribute load-bearing.
    /// </summary>
    [Fact]
    public void No_catalog_or_prompt_file_carries_a_carriage_return()
    {
        DirectoryInfo root = ProjectFile.RepositoryRoot;

        IEnumerable<FileInfo> files =
        [
            .. new DirectoryInfo(Path.Combine(root.FullName, CatalogFolder))
                .GetFiles("*", SearchOption.AllDirectories),
            .. new DirectoryInfo(Path.Combine(root.FullName, "prompts"))
                .GetFiles("*", SearchOption.AllDirectories),
        ];

        foreach (FileInfo file in files.Where(file => !file.Name.StartsWith('.')))
        {
            Assert.True(
                !File.ReadAllText(file.FullName).Contains('\r'),
                $"{file.Name} carries a carriage return. .gitattributes pins fixtures/** and prompts/** to LF so Story 2.4 hashes the same bytes on every host.");
        }
    }

    // --- the case-type spread -------------------------------------------------------------------

    [Fact]
    public void The_case_type_spread_matches_the_epic()
    {
        foreach ((CaseType type, int count) in TheEpicsSpread)
        {
            int actual = TheCaseTable.Values.Count(value => value == type);

            Assert.True(
                actual == count,
                $"The epic wants {count} {type} case(s), the table holds {actual}.");
        }

        Assert.True(
            TheCaseTable.Count == Catalog.Count,
            $"The case table lists {TheCaseTable.Count} cases, the folder holds {Catalog.Count}.");
    }

    [Fact]
    public void Plain_cases_name_an_owner_and_a_date_on_every_action()
    {
        foreach (FixtureCase fixture in OfType(CaseType.PlainOwnerAndDate))
        {
            Assert.NotEmpty(Actions(fixture.Stem));

            Assert.All(
                Actions(fixture.Stem),
                action =>
                {
                    Assert.Equal(JsonValueKind.String, Kind(action, "suggestedOwner"));
                    Assert.NotEqual(string.Empty, Text(action, "suggestedOwner"));
                    Assert.Equal(JsonValueKind.String, Kind(action, "suggestedDueDate"));
                });
        }
    }

    [Fact]
    public void No_owner_cases_leave_every_owner_empty_and_keep_the_date()
    {
        foreach (FixtureCase fixture in OfType(CaseType.NoOwner))
        {
            Assert.NotEmpty(Actions(fixture.Stem));

            Assert.All(
                Actions(fixture.Stem),
                action =>
                {
                    Assert.Equal(JsonValueKind.String, Kind(action, "suggestedOwner"));
                    Assert.Equal(string.Empty, Text(action, "suggestedOwner"));
                    Assert.Equal(JsonValueKind.String, Kind(action, "suggestedDueDate"));
                });
        }
    }

    [Fact]
    public void No_due_date_cases_leave_every_date_null_and_keep_the_owner()
    {
        foreach (FixtureCase fixture in OfType(CaseType.NoDueDate))
        {
            Assert.NotEmpty(Actions(fixture.Stem));

            Assert.All(
                Actions(fixture.Stem),
                action =>
                {
                    Assert.Equal(JsonValueKind.String, Kind(action, "suggestedOwner"));
                    Assert.NotEqual(string.Empty, Text(action, "suggestedOwner"));
                    Assert.Equal(JsonValueKind.Null, Kind(action, "suggestedDueDate"));
                });
        }
    }

    /// <summary>
    /// A relative-date case earns its category only when the answer is a resolution rather than a
    /// transcription: the resolved date must not appear anywhere in the notes the model reads.
    /// </summary>
    [Fact]
    public void Relative_date_cases_resolve_a_date_the_notes_never_spell()
    {
        foreach (FixtureCase fixture in OfType(CaseType.RelativeDate))
        {
            Assert.NotEmpty(Actions(fixture.Stem));

            Assert.All(
                Actions(fixture.Stem),
                action =>
                {
                    string dueDate = RequiredText(action, "suggestedDueDate", fixture.Stem);

                    Assert.True(
                        !fixture.Body.Contains(dueDate, StringComparison.Ordinal),
                        $"{fixture.Stem}{NotesExtension} spells {dueDate} outright, so nothing is being resolved against meetingDate.");
                });
        }
    }

    /// <summary>
    /// The check above only says the answer is not transcribed. This one says it is the right
    /// answer: each phrase resolved against its own case's <c>meetingDate</c>. The two cases are
    /// spelled out one at a time on purpose — date resolution is Story 2.4's code, and a general
    /// resolver here would be a second implementation to keep in step.
    /// </summary>
    [Fact]
    public void Each_relative_date_answer_is_its_phrase_resolved_against_its_meeting_date()
    {
        DateOnly walkthrough = MeetingDate("quarterly-safety-walkthrough");

        Assert.Equal(
            Iso(LastDayOfMonthOf(walkthrough)),
            DueDateOfTheActionQuoting("quarterly-safety-walkthrough", "by the end of this month"));

        Assert.Equal(
            Iso(walkthrough.AddDays(14)),
            DueDateOfTheActionQuoting("quarterly-safety-walkthrough", "within two weeks"));

        DateOnly inventory = MeetingDate("equipment-inventory-kickoff");

        Assert.Equal(
            Iso(LastDayOfMonthOf(new DateOnly(inventory.Year, inventory.Month, 1).AddMonths(1))),
            DueDateOfTheActionQuoting("equipment-inventory-kickoff", "by the end of next month"));
    }

    [Fact]
    public void Discussion_only_cases_expect_no_actions()
    {
        foreach (FixtureCase fixture in OfType(CaseType.DiscussionOnly))
        {
            Assert.Empty(Actions(fixture.Stem));
        }
    }

    [Fact]
    public void The_duplicate_mention_case_states_one_commitment_twice_and_expects_it_once()
    {
        FixtureCase fixture = Assert.Single(OfType(CaseType.DuplicateMention));
        IReadOnlyList<JsonObject> actions = Actions(fixture.Stem);

        int mentions = Occurrences(fixture.Body, TheDuplicatedCommitment);

        Assert.True(
            mentions >= 2,
            $"{fixture.Stem}{NotesExtension} states the duplicated commitment {mentions} time(s); the case only earns its name at two.");

        Assert.True(
            actions.Count(action => RequiredText(action, "sourceExcerpt", fixture.Stem)
                .Contains(TheDuplicatedCommitment, StringComparison.Ordinal)) == 1,
            $"{fixture.Stem}{ExpectedSuffix} must claim the duplicated commitment exactly once; two entries collapse under the Gate's one-to-one matcher and score as a false positive.");

        Assert.True(
            actions.Count == 2,
            $"{fixture.Stem}{ExpectedSuffix} holds {actions.Count} actions; the case is the duplicated commitment once plus one unrelated action.");
    }

    // --- the injection case ---------------------------------------------------------------------

    [Fact]
    public void The_injected_span_occurs_verbatim_in_its_notes()
    {
        FixtureCase fixture = Assert.Single(OfType(CaseType.Injection));

        string? span = fixture.InjectionSpan;

        Assert.NotNull(span);

        Assert.True(
            fixture.Body.Contains(span, StringComparison.Ordinal),
            $"""
             {fixture.Stem}{NotesExtension} declares an injectionSpan that is not in its notes body.
             AD-21 stores the verbatim injected text rather than an offset so the Gate's three-token
             overlap is computed against a value the file itself states.
             Span: {span}
             """);

        // An empty or one-token span would satisfy every assertion here vacuously: Contains("") is
        // true and a span under three tokens has no trigrams to collide with.
        Assert.True(
            span.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length >= 3,
            $"{fixture.Stem}{NotesExtension} declares an injectionSpan of fewer than three tokens, which makes the Gate's three-token overlap unfalsifiable. Span: {span}");
    }

    /// <summary>
    /// Story 6.2 hard-fails the injection case on any three consecutive tokens shared with the
    /// injected span, and on any unmatched extracted action. Both rules make the answer file
    /// load-bearing: it must enumerate every legitimate action, and no legitimate action may
    /// accidentally trip the overlap check.
    /// </summary>
    [Fact]
    public void No_legitimate_action_in_the_injection_case_overlaps_the_injected_span()
    {
        FixtureCase fixture = Assert.Single(OfType(CaseType.Injection));
        IReadOnlyList<JsonObject> actions = Actions(fixture.Stem);

        // An unmatched extracted action is an independent Gate hard fail, so the answer file has to
        // enumerate every legitimate action. Dropping one passes the overlap check and fails the Gate.
        Assert.True(
            actions.Count == 2,
            $"{fixture.Stem}{ExpectedSuffix} holds {actions.Count} actions; the injection case has two legitimate commitments and every one must be enumerated.");

        HashSet<string> injected = Trigrams(fixture.InjectionSpan ?? string.Empty);
        int index = 0;

        foreach (JsonObject action in actions)
        {
            foreach (string member in TheInjectionCheckedMembers)
            {
                string[] shared = [.. Trigrams(RequiredText(action, member, fixture.Stem)).Where(injected.Contains)];

                Assert.True(
                    shared.Length == 0,
                    $"{fixture.Stem}{ExpectedSuffix} action {index}'s {member} shares three consecutive tokens with the injected span: {Join(shared)}.");
            }

            index++;
        }
    }

    // --- the roster -------------------------------------------------------------------------------

    [Fact]
    public void Every_suggested_owner_resolves_through_the_roster()
    {
        HashSet<string> known = RosterNames();

        foreach (FixtureCase fixture in Catalog)
        {
            int index = 0;

            foreach (JsonObject action in Actions(fixture.Stem))
            {
                if (Text(action, "suggestedOwner") is { Length: > 0 } owner)
                {
                    Assert.True(
                        known.Contains(owner),
                        $"{fixture.Stem}{ExpectedSuffix} action {index} names owner '{owner}', which is neither a displayName nor an alias in {RosterFile}. Story 6.2 scores owner accuracy through that file.");
                }

                index++;
            }
        }
    }

    /// <summary>
    /// Story 6.1 builds each Meeting's attendee list from the front matter and its proposals from
    /// the answer file. An owner who was not in the room would be a proposal the seeded Meeting
    /// cannot explain.
    /// </summary>
    [Fact]
    public void Every_suggested_owner_attended_its_own_meeting()
    {
        foreach (FixtureCase fixture in Catalog)
        {
            int index = 0;

            foreach (JsonObject action in Actions(fixture.Stem))
            {
                if (Text(action, "suggestedOwner") is { Length: > 0 } owner)
                {
                    Assert.True(
                        fixture.Attendees.Contains(owner, StringComparer.OrdinalIgnoreCase),
                        $"{fixture.Stem}{ExpectedSuffix} action {index} names owner '{owner}', who is not in {fixture.Stem}{NotesExtension}'s attendees: {Join(fixture.Attendees)}.");
                }

                index++;
            }
        }
    }

    /// <summary>
    /// Story 6.2 resolves an extracted owner to a person by looking the string up across every
    /// display name and alias. A name that appears under two people makes that lookup ambiguous,
    /// and the scorer has no way to pick.
    /// </summary>
    [Fact]
    public void No_roster_name_or_alias_belongs_to_two_people()
    {
        List<string> everyName = [];

        foreach (JsonObject person in RosterPeople())
        {
            if (person["displayName"]?.GetValue<string>() is { } displayName)
            {
                everyName.Add(displayName);
            }

            everyName.AddRange((person["aliases"]?.AsArray() ?? new JsonArray())
                .Select(alias => alias?.GetValue<string>() ?? string.Empty));
        }

        string[] repeated =
        [
            .. everyName
                .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key),
        ];

        Assert.True(
            repeated.Length == 0,
            $"{RosterFile} gives the same name to more than one person, so Story 6.2's owner lookup is ambiguous: {Join(repeated)}.");
    }

    [Fact]
    public void The_roster_carries_the_seeded_display_names_and_no_credentials()
    {
        JsonObject roster = RosterDocument();
        JsonArray people = roster["people"]?.AsArray()
            ?? throw new InvalidOperationException($"{RosterFile} has no 'people' array.");

        string[] displayNames =
        [
            .. people
                .Select(person => person?.AsObject()["displayName"]?.GetValue<string>() ?? string.Empty)
                .OrderBy(name => name, StringComparer.Ordinal),
        ];

        string[] seeded = [.. TheRosterDisplayNames.OrderBy(name => name, StringComparer.Ordinal)];

        Assert.Equal(seeded, displayNames);

        Assert.All(
            people,
            person =>
            {
                JsonObject entry = RosterPerson(person);

                string displayName = entry["displayName"]?.GetValue<string>() ?? string.Empty;

                // The role is read off the seeder for the same reason the display names are: a
                // hand-copied role lets the roster and this test agree while both disagree with
                // DemoDataSeeder, and Story 6.1 seeds the user the seeder describes, not this file.
                string seededRole = DemoDataSeeder.DemoUsers
                    .Single(user => user.DisplayName == displayName)
                    .Role
                    .ToString();

                Assert.Equal(seededRole, entry["role"]?.GetValue<string>());
                Assert.NotEmpty(RosterAliases(entry));
            });

        // The PRD addendum's aliases, plus the exact string the 0.55 equipment-inventory proposal carries.
        JsonObject priya = people
            .Select(person => person!.AsObject())
            .Single(person => person["displayName"]?.GetValue<string>() == "Priya Ramaswamy");

        string[] aliases = [.. priya["aliases"]!.AsArray().Select(alias => alias!.GetValue<string>())];

        foreach (string alias in ThePriyaAliases)
        {
            Assert.Contains(alias, aliases);
        }

        string text = ReadCatalogFile(RosterFile);

        foreach (string secret in TheCredentialWords)
        {
            Assert.DoesNotContain(secret, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    // --- reading the catalog --------------------------------------------------------------------

    private enum CaseType
    {
        PlainOwnerAndDate,
        NoOwner,
        NoDueDate,
        RelativeDate,
        DiscussionOnly,
        DuplicateMention,
        Injection,
    }

    /// <param name="Stem">The kebab-case case key, shared by the two files.</param>
    /// <param name="Title">The <c>title</c> front-matter value, or null when it is absent.</param>
    /// <param name="MeetingDate">The <c>meetingDate</c> front-matter value, or null when absent.</param>
    /// <param name="Attendees">The <c>attendees</c> flow sequence, empty when absent.</param>
    /// <param name="Seed">The raw <c>seed</c> value, so a non-boolean fails its own assertion.</param>
    /// <param name="InjectionSpan">The verbatim injected text, or null on the thirteen clean cases.</param>
    /// <param name="Body">The notes body, byte-for-byte after the closing delimiter's newline.</param>
    /// <param name="Expected">The parsed <c>.expected.json</c> document root.</param>
    private sealed record FixtureCase(
        string Stem,
        string? Title,
        string? MeetingDate,
        IReadOnlyList<string> Attendees,
        string? Seed,
        string? InjectionSpan,
        string Body,
        JsonObject Expected);

    private static IReadOnlyList<FixtureCase> LoadCatalog()
    {
        DirectoryInfo folder = CatalogDirectory();
        List<FixtureCase> cases = [];

        IEnumerable<FileInfo> notesFiles = folder
            .GetFiles($"*{NotesExtension}")
            .Where(file => !string.Equals(file.Name, ReadmeFile, StringComparison.Ordinal))
            .OrderBy(file => file.Name, StringComparer.Ordinal);

        foreach (FileInfo notes in notesFiles)
        {
            string stem = notes.Name[..^NotesExtension.Length];
            string raw = File.ReadAllText(notes.FullName);
            (string frontMatter, string body) = SplitFrontMatter(stem, raw);

            string expectedPath = Path.Combine(folder.FullName, $"{stem}{ExpectedSuffix}");

            Assert.True(
                File.Exists(expectedPath),
                $"Expected {stem}{ExpectedSuffix} beside {stem}{NotesExtension}. AD-21 pairs every case with its answer.");

            JsonObject expected = ParseObject(File.ReadAllText(expectedPath), $"{stem}{ExpectedSuffix}");

            cases.Add(new FixtureCase(
                stem,
                Scalar(frontMatter, "title"),
                Scalar(frontMatter, "meetingDate"),
                Sequence(frontMatter, "attendees"),
                Scalar(frontMatter, "seed"),
                Scalar(frontMatter, "injectionSpan"),
                body,
                expected));
        }

        return cases;
    }

    /// <summary>
    /// One root-finder per assembly: <c>ProjectFile.RepositoryRoot</c> walks up to
    /// <c>ActionLedger.sln</c>, the way <c>ComposeTopologyTests</c> and <c>WebStructureTests</c>
    /// already do.
    /// </summary>
    private static DirectoryInfo CatalogDirectory()
    {
        DirectoryInfo folder = new(Path.Combine(ProjectFile.RepositoryRoot.FullName, CatalogFolder));

        Assert.True(
            folder.Exists,
            $"Expected the fixture catalog at {folder.FullName}. AD-21 puts it at the repository root.");

        return folder;
    }

    private static string ReadCatalogFile(string name)
    {
        string path = Path.Combine(CatalogDirectory().FullName, name);

        Assert.True(File.Exists(path), $"Expected {CatalogFolder}/{name} at {path}.");

        return File.ReadAllText(path);
    }

    /// <summary>
    /// The notes body is everything after the line containing the closing <c>---</c>, with that
    /// line's terminating newline consumed and the remainder kept byte-for-byte. Story 2.4 hashes
    /// this text and its verifier searches it, so trimming here would disagree with the answers.
    /// </summary>
    private static (string FrontMatter, string Body) SplitFrontMatter(string stem, string raw)
    {
        Match delimited = Regex.Match(
            raw,
            @"\A---\r?\n(?<front>.*?)^---[ \t]*\r?\n",
            RegexOptions.Singleline | RegexOptions.Multiline);

        Assert.True(
            delimited.Success,
            $"{stem}{NotesExtension} does not open with '---' front matter closed by a '---' line. AD-21 requires it.");

        return (delimited.Groups["front"].Value, raw[delimited.Length..]);
    }

    private static string? Scalar(string frontMatter, string key)
    {
        Match assignment = Regex.Match(
            frontMatter,
            $@"^{Regex.Escape(key)}:[ \t]*(?<value>.*?)[ \t]*\r?$",
            RegexOptions.Multiline);

        return assignment.Success ? Unquote(assignment.Groups["value"].Value) : null;
    }

    private static IReadOnlyList<string> Sequence(string frontMatter, string key)
    {
        string? value = Scalar(frontMatter, key);

        if (value is null || !value.StartsWith('[') || !value.EndsWith(']'))
        {
            return [];
        }

        return
        [
            .. value[1..^1]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Unquote),
        ];
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0]
            ? value[1..^1]
            : value;

    // --- reading one case -------------------------------------------------------------------------

    private static FixtureCase Case(string stem) =>
        Catalog.SingleOrDefault(fixture => string.Equals(fixture.Stem, stem, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"{CatalogFolder} holds no case '{stem}'.");

    private static IEnumerable<FixtureCase> OfType(CaseType type) =>
        Catalog.Where(fixture => TheCaseTable.TryGetValue(fixture.Stem, out CaseType value) && value == type);

    private static IReadOnlyList<JsonObject> Actions(string stem)
    {
        JsonArray? actions = Case(stem).Expected["actions"]?.AsArray();

        Assert.True(actions is not null, $"{stem}{ExpectedSuffix} has no 'actions' array.");

        return [.. actions!.Select(action => action?.AsObject() ?? throw new InvalidOperationException($"{stem}{ExpectedSuffix} holds a null action."))];
    }

    private static JsonValueKind Kind(JsonObject action, string member) =>
        action.TryGetPropertyValue(member, out JsonNode? node)
            ? node?.GetValueKind() ?? JsonValueKind.Null
            : JsonValueKind.Undefined;

    private static string? Text(JsonObject action, string member) =>
        Kind(action, member) is JsonValueKind.String ? action[member]!.GetValue<string>() : null;

    private static double Number(JsonObject action, string member) =>
        Kind(action, member) is JsonValueKind.Number ? action[member]!.GetValue<double>() : double.NaN;

    private static string RequiredText(JsonObject action, string member, string where)
    {
        string? text = Text(action, member);

        Assert.True(text is not null, $"{where}: {member} is {Kind(action, member)}; a string is required here.");

        return text!;
    }

    // --- small helpers ----------------------------------------------------------------------------

    /// <summary>
    /// The catalog's files, minus the dot-prefixed ones a file manager leaves behind. A stray
    /// <c>.DS_Store</c> is not a second source of truth; a subdirectory would be, so the membership
    /// test forbids those outright.
    /// </summary>
    private static IEnumerable<FileInfo> VisibleFiles(DirectoryInfo folder) =>
        folder.GetFiles().Where(file => !file.Name.StartsWith('.'));

    private static DateOnly MeetingDate(string stem)
    {
        string? value = Case(stem).MeetingDate;

        Assert.True(
            IsIsoDate(value),
            $"{stem}{NotesExtension} declares meetingDate '{value}'; AD-21 requires YYYY-MM-DD.");

        return DateOnly.ParseExact(value!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static DateOnly LastDayOfMonthOf(DateOnly date) =>
        new(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// The due date of the one action in <paramref name="stem"/> whose excerpt carries
    /// <paramref name="phrase"/>. Single, not First: two actions quoting the same relative phrase
    /// would make the assertion about it meaningless.
    /// </summary>
    private static string DueDateOfTheActionQuoting(string stem, string phrase)
    {
        JsonObject action = Assert.Single(
            Actions(stem),
            candidate => RequiredText(candidate, "sourceExcerpt", stem).Contains(phrase, StringComparison.Ordinal));

        return RequiredText(action, "suggestedDueDate", stem);
    }

    private static IReadOnlyList<JsonObject> RosterPeople()
    {
        JsonArray people = RosterDocument()["people"]?.AsArray()
            ?? throw new InvalidOperationException($"{RosterFile} has no 'people' array.");

        return
        [
            .. people.Select(person =>
                person?.AsObject() ?? throw new InvalidOperationException($"{RosterFile} holds a null person.")),
        ];
    }

    private static HashSet<string> RosterNames()
    {
        JsonArray people = RosterDocument()["people"]?.AsArray()
            ?? throw new InvalidOperationException($"{RosterFile} has no 'people' array.");

        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);

        foreach (JsonObject person in people.Select(RosterPerson))
        {
            if (person["displayName"]?.GetValue<string>() is { } displayName)
            {
                names.Add(displayName);
            }

            foreach (JsonNode? alias in RosterAliases(person))
            {
                if (alias?.GetValue<string>() is { } text)
                {
                    names.Add(text);
                }
            }
        }

        return names;
    }

    private static JsonObject RosterDocument() => ParseObject(ReadCatalogFile(RosterFile), RosterFile);

    /// <summary>
    /// <c>Ai:LowConfidenceThreshold</c> as the API ships it. Read as text for the reason the rest
    /// of this assembly reads repository artefacts as text: the value is a shipped constant, and
    /// retyping it here is the drift this file exists to prevent.
    /// </summary>
    private static double ConfiguredLowConfidenceThreshold()
    {
        string settings = File.ReadAllText(
            Path.Combine(ProjectFile.RepositoryRoot.FullName, "src/ActionLedger.Api/appsettings.json"));

        Match configured = Regex.Match(settings, @"""LowConfidenceThreshold""\s*:\s*(?<value>[0-9.]+)");

        Assert.True(configured.Success, "appsettings.json declares no Ai:LowConfidenceThreshold.");

        return double.Parse(configured.Groups["value"].Value, CultureInfo.InvariantCulture);
    }

    /// <summary>One roster entry, named in the message when it is not an object.</summary>
    private static JsonObject RosterPerson(JsonNode? entry) =>
        entry as JsonObject
        ?? throw new InvalidOperationException($"{RosterFile} holds a person that is not a JSON object.");

    /// <summary>One roster entry's aliases, named in the message when the member is not an array.</summary>
    private static JsonArray RosterAliases(JsonObject person) =>
        person["aliases"] as JsonArray
        ?? throw new InvalidOperationException(
            $"{RosterFile}: {person["displayName"]?.GetValue<string>() ?? "a person"} has no 'aliases' array.");

    /// <summary>
    /// Parse one catalog file into a JSON object, naming the file on every failure path.
    /// <c>Catalog</c> is a static field, so an exception escaping here surfaces as a
    /// <c>TypeInitializationException</c> on every test in the class; without the file name in the
    /// message, one mistyped fixture reports as forty opaque failures and names none of them.
    /// </summary>
    private static JsonObject ParseObject(string text, string fileName)
    {
        JsonNode? parsed;

        try
        {
            parsed = JsonNode.Parse(text);
        }
        catch (JsonException error)
        {
            throw new InvalidOperationException($"{fileName} is not valid JSON: {error.Message}", error);
        }

        return parsed as JsonObject
            ?? throw new InvalidOperationException($"{fileName} is not a JSON object.");
    }

    /// <summary>
    /// Three consecutive whitespace-separated tokens, the unit Story 6.2's injection hard fail is
    /// computed in. Comparison is case-insensitive, which is stricter than the Gate needs.
    /// </summary>
    private static HashSet<string> Trigrams(string text)
    {
        string[] tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        HashSet<string> trigrams = new(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index + 2 < tokens.Length; index++)
        {
            trigrams.Add($"{tokens[index]} {tokens[index + 1]} {tokens[index + 2]}");
        }

        return trigrams;
    }

    private static int Occurrences(string haystack, string needle)
    {
        int count = 0;

        for (int at = haystack.IndexOf(needle, StringComparison.Ordinal); at >= 0;
             at = haystack.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static bool IsIsoDate(string? value) =>
        value is not null
        && Regex.IsMatch(value, @"\A\d{4}-\d{2}-\d{2}\z")
        && DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    private static string Join(IEnumerable<string> values) =>
        string.Join(", ", values.OrderBy(value => value, StringComparer.Ordinal));
}
