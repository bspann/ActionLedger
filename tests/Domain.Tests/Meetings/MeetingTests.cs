using System.Security.Cryptography;
using System.Text;
using ActionLedger.Domain.Common;
using ActionLedger.Domain.Meetings;
using Xunit;

namespace ActionLedger.Domain.Tests.Meetings;

/// <summary>
/// AD-3 and AD-5 / ADR-002 — the aggregate is where "notes attach exactly once and are stored
/// byte-for-byte" is made unforgettable. Nothing above the Domain restates these rules, so if they
/// are not true here they are not true anywhere.
/// </summary>
public sealed class MeetingTests
{
    private static readonly DateOnly Held = new(2026, 9, 18);

    private static readonly DateTimeOffset Created = new(2026, 9, 18, 14, 30, 0, TimeSpan.Zero);

    private static readonly Guid Actor = Guid.CreateVersion7();

    [Fact]
    public void A_meeting_records_the_actor_the_caller_supplied_and_nothing_it_invented()
    {
        Meeting meeting = Meeting.Create("Weekly sync", Held, ["Dana Whitfield"], Actor, Created);

        Assert.Equal(Actor, meeting.CreatedByUserId);
        Assert.Equal(Held, meeting.MeetingDate);
        Assert.Equal(Created, meeting.CreatedAt);
        Assert.Equal(["Dana Whitfield"], meeting.Attendees);
        Assert.False(meeting.HasNotes);
        Assert.Null(meeting.Notes);
    }

    [Fact]
    public void An_id_is_a_uuidv7_the_constructor_made()
    {
        Meeting meeting = Meeting.Create("Weekly sync", Held, null, Actor, Created);

        Assert.NotEqual(Guid.Empty, meeting.Id);
        Assert.Equal(7, (meeting.Id.ToByteArray(bigEndian: true)[6] & 0xF0) >> 4);
    }

    [Fact]
    public void A_meeting_with_no_attendees_carries_an_empty_list_rather_than_null()
    {
        Assert.Empty(Meeting.Create("Weekly sync", Held, null, Actor, Created).Attendees);
        Assert.Empty(Meeting.Create("Weekly sync", Held, [], Actor, Created).Attendees);
    }

    [Fact]
    public void The_created_instant_is_stored_in_utc_whatever_offset_it_arrived_as()
    {
        Meeting meeting = Meeting.Create(
            "Weekly sync",
            Held,
            null,
            Actor,
            new DateTimeOffset(2026, 9, 18, 9, 30, 0, TimeSpan.FromHours(-5)));

        Assert.Equal(TimeSpan.Zero, meeting.CreatedAt.Offset);
        Assert.Equal(new DateTimeOffset(2026, 9, 18, 14, 30, 0, TimeSpan.Zero), meeting.CreatedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void A_blank_title_is_refused(string title) =>
        Assert.Throws<DomainRuleException>(() => Meeting.Create(title, Held, null, Actor, Created));

    [Fact]
    public void A_title_longer_than_the_column_is_refused()
    {
        Assert.Throws<DomainRuleException>(() =>
            Meeting.Create(new string('t', Meeting.TitleMaxLength + 1), Held, null, Actor, Created));

        // The boundary itself is allowed, so the constant and the column agree on one number.
        Assert.Equal(
            Meeting.TitleMaxLength,
            Meeting.Create(new string('t', Meeting.TitleMaxLength), Held, null, Actor, Created).Title.Length);
    }

    [Fact]
    public void The_title_is_trimmed_so_the_unique_index_cannot_be_dodged_with_a_space()
    {
        // meeting(title, meeting_date) is unique. Without this, "Standup" and "Standup " would be
        // two meetings on the same day, which is exactly the duplicate the rule refuses.
        Assert.Equal("Standup", Meeting.Create("  Standup  ", Held, null, Actor, Created).Title);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void A_blank_attendee_name_is_refused(string attendee) =>
        Assert.Throws<DomainRuleException>(() => Meeting.Create("Weekly sync", Held, [attendee], Actor, Created));

    [Fact]
    public void An_attendee_name_of_exactly_the_limit_is_accepted() =>
        Assert.Equal(
            Meeting.AttendeeMaxLength,
            Meeting
                .Create("Weekly sync", Held, [new string('a', Meeting.AttendeeMaxLength)], Actor, Created)
                .Attendees[0]
                .Length);

    [Fact]
    public void An_attendee_name_longer_than_the_limit_is_refused() =>
        Assert.Throws<DomainRuleException>(() => Meeting.Create(
            "Weekly sync",
            Held,
            [new string('a', Meeting.AttendeeMaxLength + 1)],
            Actor,
            Created));

    [Fact]
    public void Attendee_names_are_trimmed()
    {
        Meeting meeting = Meeting.Create("Weekly sync", Held, ["  Dana Whitfield ", "Marcus Bell\t"], Actor, Created);

        Assert.Equal(["Dana Whitfield", "Marcus Bell"], meeting.Attendees);
    }

    [Fact]
    public void A_meeting_with_no_creator_is_refused() =>
        Assert.Throws<DomainRuleException>(() => Meeting.Create("Weekly sync", Held, null, Guid.Empty, Created));

    [Fact]
    public void Attaching_notes_is_what_turns_has_notes_true()
    {
        Meeting meeting = Meeting.Create("Weekly sync", Held, null, Actor, Created);

        Assert.False(meeting.HasNotes);

        MeetingNotes notes = meeting.AttachNotes("Dana will send the draft by Friday.", Created);

        Assert.True(meeting.HasNotes);
        Assert.Same(notes, meeting.Notes);
        Assert.Equal(meeting.Id, notes.MeetingId);
        Assert.Equal(7, (notes.Id.ToByteArray(bigEndian: true)[6] & 0xF0) >> 4);
    }

    [Fact]
    public void Attaching_notes_a_second_time_is_a_rule_violation_not_an_update()
    {
        Meeting meeting = Meeting.Create("Weekly sync", Held, null, Actor, Created);

        meeting.AttachNotes("The first paste.", Created);

        Assert.Throws<DomainRuleException>(() => meeting.AttachNotes("A replacement.", Created));

        // And the first text is still what the aggregate holds.
        Assert.Equal("The first paste.", meeting.Notes!.Text);
    }

    [Fact]
    public void Nothing_on_the_aggregate_can_change_or_clear_notes()
    {
        // ADR-002 is a shape claim, not only a behaviour claim: if a mutator existed, some caller
        // would eventually find it. There must be no public member that writes Notes at all.
        // An allowlist over what Meeting itself declares, rather than a filter on the word "Notes":
        // a mutator called Overwrite or Reset writes Notes just as well as one whose name says so,
        // and a name filter would wave it through. Scoped to DeclaringType so inherited members
        // (AggregateRoot's own event bookkeeping) are somebody else's rule — which is exactly how
        // the MeetingNotes branches below have always worked.
        string[] writers =
        [
            .. typeof(Meeting)
                .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Where(method => !method.IsSpecialName && method.DeclaringType == typeof(Meeting))
                .Where(method => method.Name != nameof(Meeting.AttachNotes))
                .Select(method => $"{nameof(Meeting)}.{method.Name}"),
            .. typeof(Meeting)
                .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Where(property => property.SetMethod is { IsPublic: true })
                .Select(property => $"{nameof(Meeting)}.{property.Name}"),
            .. typeof(MeetingNotes)
                .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Where(property => property.SetMethod is { IsPublic: true })
                .Select(property => $"{nameof(MeetingNotes)}.{property.Name}"),
            .. typeof(MeetingNotes)
                .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Where(method => !method.IsSpecialName && method.DeclaringType == typeof(MeetingNotes))
                .Select(method => $"{nameof(MeetingNotes)}.{method.Name}"),
        ];

        Assert.True(
            writers.Length == 0,
            $"Notes are write-once (AD-5, ADR-002). Remove: {string.Join(", ", writers)}");
    }

    [Theory]
    [InlineData("  leading and trailing  ")]
    [InlineData("first line\r\nsecond line\r\n")]
    [InlineData("tabs\tand\ttrailing spaces   ")]
    [InlineData("\n\nblank lines survive\n\n")]
    public void Notes_are_stored_byte_for_byte_with_no_trim_and_no_newline_normalization(string pasted)
    {
        Meeting meeting = Meeting.Create("Weekly sync", Held, null, Actor, Created);

        MeetingNotes notes = meeting.AttachNotes(pasted, Created);

        // Ordinal, and the raw UTF-8 bytes too: FR-2 says byte-for-byte, and the hash is only
        // meaningful if that holds.
        Assert.Equal(pasted, notes.Text);
        Assert.Equal(Encoding.UTF8.GetBytes(pasted), Encoding.UTF8.GetBytes(notes.Text));
    }

    [Fact]
    public void The_hash_is_lower_case_hex_sha256_of_the_utf8_bytes_of_the_stored_text()
    {
        Meeting meeting = Meeting.Create("Weekly sync", Held, null, Actor, Created);

        // The published SHA-256 of "abc" — a vector this repository did not compute for itself.
        MeetingNotes notes = meeting.AttachNotes("abc", Created);

        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", notes.Sha256);
        Assert.Equal(MeetingNotes.Sha256Length, notes.Sha256.Length);
    }

    [Fact]
    public void The_hash_is_of_the_text_that_was_stored_including_its_whitespace()
    {
        Meeting meeting = Meeting.Create("Weekly sync", Held, null, Actor, Created);

        const string pasted = "  Dana will send the draft.\r\n";

        MeetingNotes notes = meeting.AttachNotes(pasted, Created);

        Assert.Equal(
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(pasted))),
            notes.Sha256);

        // And a trimmed copy hashes to something else, so the assertion above is not a tautology.
        Assert.NotEqual(
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(pasted.Trim()))),
            notes.Sha256);
    }

    [Fact]
    public void Empty_notes_are_refused()
    {
        Meeting meeting = Meeting.Create("Weekly sync", Held, null, Actor, Created);

        Assert.Throws<DomainRuleException>(() => meeting.AttachNotes(string.Empty, Created));
        Assert.False(meeting.HasNotes);
    }

    [Fact]
    public void Notes_longer_than_the_column_are_refused()
    {
        Meeting meeting = Meeting.Create("Weekly sync", Held, null, Actor, Created);

        Assert.Throws<DomainRuleException>(() =>
            meeting.AttachNotes(new string('n', MeetingNotes.TextMaxLength + 1), Created));

        // The boundary itself is allowed, so the constant, the annotation, and the column agree.
        Assert.Equal(
            MeetingNotes.TextMaxLength,
            meeting.AttachNotes(new string('n', MeetingNotes.TextMaxLength), Created).Text.Length);
    }

    [Fact]
    public void Whitespace_only_notes_are_kept_because_only_length_is_guarded()
    {
        // The guard is length, not meaning. A note of one space is a note; the API's [Required]
        // is what refuses it at the boundary, and the Domain does not second-guess the text.
        Meeting meeting = Meeting.Create("Weekly sync", Held, null, Actor, Created);

        Assert.Equal(" ", meeting.AttachNotes(" ", Created).Text);
    }

    [Fact]
    public void The_saved_instant_is_stored_in_utc()
    {
        Meeting meeting = Meeting.Create("Weekly sync", Held, null, Actor, Created);

        MeetingNotes notes = meeting.AttachNotes(
            "Dana will send the draft.",
            new DateTimeOffset(2026, 9, 18, 9, 30, 0, TimeSpan.FromHours(-5)));

        Assert.Equal(TimeSpan.Zero, notes.SavedAt.Offset);
        Assert.Equal(new DateTimeOffset(2026, 9, 18, 14, 30, 0, TimeSpan.Zero), notes.SavedAt);
    }
}
