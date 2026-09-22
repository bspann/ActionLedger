using System.Globalization;
using System.Reflection;
using System.Text;
using ActionLedger.Web.Core;
using ActionLedger.Web.Core.Formatting;
using Xunit;

namespace ActionLedger.Web.Tests;

/// <summary>
/// UX-DR20 — EXPERIENCE.md says its strings are used verbatim and its two date shapes are fixed.
/// This is what makes "verbatim" checkable: every <see cref="Voice"/> constant is pinned to its
/// literal and compared ordinally, so a single changed character — including the U+2019
/// apostrophe in <c>Couldn't load.</c> — fails naming the constant.
/// </summary>
public sealed class VoiceAndFormatsTests
{
    /// <summary>
    /// Every constant on <see cref="Voice"/>, by name. The completeness test below compares this
    /// set against the class by reflection, so a constant added without a row here also fails.
    /// </summary>
    public static TheoryData<string, string> VoiceConstants => new()
    {
        { nameof(Voice.ProductName), "ActionLedger" },
        { nameof(Voice.Meetings), "Meetings" },
        { nameof(Voice.Actions), "Actions" },
        { nameof(Voice.SignIn), "Sign in" },
        { nameof(Voice.SignOut), "Sign out" },
        { nameof(Voice.SignInFailed), "Sign-in failed. Check your username and password." },
        { nameof(Voice.Username), "Username" },
        { nameof(Voice.Password), "Password" },
        { nameof(Voice.Retry), "Retry" },
        { nameof(Voice.NotFound), "Not found." },
        { nameof(Voice.LoadFailurePrefix), "Couldn’t load. " },
        { nameof(Voice.UnexpectedFailureTitle), "An unexpected error occurred." },
        { nameof(Voice.SessionExpired), "Session expired. Sign in again." },
        { nameof(Voice.RoleNotAllowed), "Your role does not allow this." },
        { nameof(Voice.AlreadyChanged), "Already changed. Reloading." },
        { nameof(Voice.NewMeeting), "New meeting" },
        { nameof(Voice.NoMeetings), "No meetings yet." },
        { nameof(Voice.Title), "Title" },
        { nameof(Voice.Date), "Date" },
        { nameof(Voice.Runs), "Runs" },
        { nameof(Voice.TrackedActions), "Tracked Actions" },
        { nameof(Voice.Attendees), "Attendees" },
        { nameof(Voice.Create), "Create" },
        { nameof(Voice.Cancel), "Cancel" },
        { nameof(Voice.Meeting), "Meeting" },
        { nameof(Voice.Notes), "Notes" },
        { nameof(Voice.SaveNotes), "Save notes" },
        { nameof(Voice.NotesSaved), "Notes saved." },
        { nameof(Voice.NotesImmutable), "Notes cannot be changed after saving" },
        { nameof(Voice.NotesSavedPrefix), "Saved " },
        { nameof(Voice.NotesSavedSuffix), ", immutable" },
        { nameof(Voice.TitleRequired), "Title is required." },
        { nameof(Voice.TitleTooLong), "Title must be 200 characters or fewer." },
        { nameof(Voice.DateRequired), "Date is required." },
        { nameof(Voice.AttendeeTooLong), "An attendee must be 100 characters or fewer." },
        { nameof(Voice.NoValue), "-" },
    };

    [Theory]
    [MemberData(nameof(VoiceConstants))]
    public void The_voice_constant_reads_exactly_as_experience_md_writes_it(string name, string expected)
    {
        string actual = Declared()[name];

        Assert.True(
            string.Equals(expected, actual, StringComparison.Ordinal),
            $"Voice.{name} must read \"{Describe(expected)}\" verbatim, but reads \"{Describe(actual)}\".");
    }

    [Fact]
    public void Every_voice_constant_is_pinned()
    {
        // Otherwise a new constant could be added, rendered on screen, and never checked against
        // EXPERIENCE.md — the pinning above would still be green on the strings it happens to know.
        string[] pinned = [.. VoiceConstants.Select(row => row.Data.Item1).Order(StringComparer.Ordinal)];
        string[] declared = [.. Declared().Keys.Order(StringComparer.Ordinal)];

        Assert.Equal(pinned, declared);
    }

    [Theory]
    [MemberData(nameof(VoiceConstants))]
    public void The_voice_constant_carries_no_exclamation_mark_and_no_emoji(string name, string expected)
    {
        // EXPERIENCE.md, Voice and Tone: "Plain, declarative, no exclamation marks, no emoji."
        // The one character above ASCII that this vocabulary is allowed is the typographic
        // apostrophe EXPERIENCE.md itself writes.
        Assert.DoesNotContain('!', expected);

        foreach (Rune rune in expected.EnumerateRunes())
        {
            Assert.True(
                rune.Value < 0x80 || rune.Value == '’',
                $"Voice.{name} contains U+{rune.Value:X4}, which is neither ASCII nor the U+2019 apostrophe.");
        }
    }

    [Fact]
    public void A_date_renders_as_the_iso_calendar_date()
    {
        Assert.Equal("2026-10-03", Formats.Date(new DateOnly(2026, 10, 3)));
    }

    [Fact]
    public void An_instant_renders_in_utc_with_the_suffix()
    {
        DateTimeOffset instant = new(2026, 9, 21, 14, 3, 0, TimeSpan.Zero);

        Assert.Equal("2026-09-21 14:03 UTC", Formats.Instant(instant));
    }

    [Fact]
    public void An_instant_in_another_offset_is_converted_to_utc_first()
    {
        // The same moment, written with a +05:30 offset. The Audit Trail is evidence, so the
        // rendered time may not move with whatever offset the server or browser attached.
        DateTimeOffset instant = new(2026, 9, 21, 19, 33, 0, TimeSpan.FromMinutes(330));

        Assert.Equal("2026-09-21 14:03 UTC", Formats.Instant(instant));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("ar-SA")]
    public void Both_shapes_ignore_the_current_culture(string culture)
    {
        // A WebAssembly host carries the browser's culture. Under de-DE a CurrentCulture format
        // renders 21.09.2026, and under ar-SA it renders a Hijri date entirely.
        CultureInfo original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);

        try
        {
            Assert.Equal("2026-10-03", Formats.Date(new DateOnly(2026, 10, 3)));
            Assert.Equal(
                "2026-09-21 14:03 UTC",
                Formats.Instant(new DateTimeOffset(2026, 9, 21, 14, 3, 0, TimeSpan.Zero)));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void The_immutable_notes_caption_reads_as_experience_md_writes_it()
    {
        // EXPERIENCE.md: a caption "Saved {timestamp}, immutable". The sentence is assembled from
        // the prefix and suffix rather than typed into the page, so this is where it is checked.
        DateTimeOffset saved = new(2026, 9, 21, 14, 3, 0, TimeSpan.Zero);

        Assert.Equal(
            "Saved 2026-09-21 14:03 UTC, immutable",
            Voice.NotesSavedPrefix + Formats.Instant(saved) + Voice.NotesSavedSuffix);
    }

    [Fact]
    public void A_count_renders_against_its_limit_with_group_separators()
    {
        // The notes paste area's live count. EXPERIENCE.md's limit is 50,000.
        Assert.Equal("0 / 50,000", Formats.Count(0, 50_000));
        Assert.Equal("1,234 / 50,000", Formats.Count(1_234, 50_000));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("ar-SA")]
    public void The_count_ignores_the_current_culture(string culture)
    {
        // Under de-DE an N0 format renders the limit as 50.000, and under ar-SA with Eastern
        // Arabic digits. A WebAssembly host carries whatever culture the browser has.
        CultureInfo original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);

        try
        {
            Assert.Equal("1,234 / 50,000", Formats.Count(1_234, 50_000));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    private static Dictionary<string, string> Declared() =>
        typeof(Voice)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(field => field is { IsLiteral: true, IsInitOnly: false })
            .ToDictionary(field => field.Name, field => (string)field.GetRawConstantValue()!, StringComparer.Ordinal);

    /// <summary>Escapes anything above ASCII, so a failure over one apostrophe is readable.</summary>
    private static string Describe(string value) =>
        string.Concat(value.Select(character =>
            character < 0x80 ? character.ToString() : $"\\u{(int)character:X4}"));
}
