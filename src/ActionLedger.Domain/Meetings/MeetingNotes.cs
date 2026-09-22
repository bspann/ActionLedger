using System.Security.Cryptography;
using System.Text;
using ActionLedger.Domain.Common;

namespace ActionLedger.Domain.Meetings;

/// <summary>
/// ADR-002 / AD-5 — the notes pasted into a <see cref="Meeting"/>, stored byte-for-byte and never
/// changed. There is no update method, no clear method, and no public setter of any kind; the only
/// way one exists is <see cref="Meeting.AttachNotes"/>, and the only way a second one could exist
/// is a rule violation the aggregate refuses.
/// </summary>
/// <remarks>
/// <para>
/// The text is stored exactly as submitted: no trim, no newline normalization, no re-encoding.
/// <see cref="Sha256"/> is computed here, at construction, over the UTF-8 bytes of that same
/// string, so "the hash is of the stored text" is structurally true rather than conventionally
/// true.
/// </para>
/// <para>
/// It is not tamper-evidence. The digest is unsigned and lives in the same <c>meeting_notes</c>
/// row as the text, so whatever can rewrite one can rewrite the other in the same statement. What
/// it gives is a short, stable identity for an exact byte sequence: an extraction run records the
/// hash of the notes it read, so two runs can be told apart or matched without re-reading 50,000
/// characters, and accidental corruption — a truncated write, a re-encoded restore — shows up as
/// a mismatch.
/// </para>
/// <para>
/// This is a child entity of the <see cref="Meeting"/> aggregate, not a root, so it does not
/// inherit <c>AggregateRoot</c>: it has no domain events and no independent lifetime. Its id is
/// still a UUIDv7 made in the constructor, for the same reason every other id is (AD-10).
/// </para>
/// </remarks>
public sealed class MeetingNotes
{
    /// <summary>The longest note the paste area accepts, and the column's declared length.</summary>
    public const int TextMaxLength = 50_000;

    /// <summary>The length of a SHA-256 digest in lower-case hex.</summary>
    public const int Sha256Length = 64;

    /// <summary>
    /// Only <see cref="Meeting.AttachNotes"/> reaches this. It is <c>internal</c> rather than
    /// public so nothing outside the aggregate can mint notes that were never attached to one.
    /// </summary>
    internal MeetingNotes(Guid meetingId, string text, DateTimeOffset savedAt)
    {
        Id = Guid.CreateVersion7();
        MeetingId = meetingId;
        Text = RequireText(text);
        Sha256 = HashOf(Text);
        SavedAt = savedAt;
    }

    /// <summary>Rehydration constructor for EF Core.</summary>
    private MeetingNotes()
    {
        Text = string.Empty;
        Sha256 = string.Empty;
    }

    /// <summary>The notes' identity: a UUIDv7 from <see cref="Guid.CreateVersion7()"/>.</summary>
    public Guid Id { get; private set; }

    /// <summary>The Meeting these notes belong to. <c>meeting_notes(meeting_id)</c> is unique.</summary>
    public Guid MeetingId { get; private set; }

    /// <summary>
    /// The notes, byte-for-byte as they were submitted — including leading and trailing
    /// whitespace and whatever line endings were pasted. Nothing normalizes this.
    /// </summary>
    public string Text { get; private set; }

    /// <summary>Lower-case hex SHA-256 of the UTF-8 bytes of <see cref="Text"/>, 64 characters.</summary>
    public string Sha256 { get; private set; }

    /// <summary>When the notes were saved, from <c>IClock</c> (AD-15).</summary>
    public DateTimeOffset SavedAt { get; private set; }

    private static string HashOf(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>
    /// Guards length and nothing else. A note of only whitespace is still a note; what is refused
    /// is nothing at all, and more than the column can hold.
    /// </summary>
    private static string RequireText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            throw new DomainRuleException("Notes cannot be empty.");
        }

        return text.Length > TextMaxLength
            ? throw new DomainRuleException($"Notes cannot exceed {TextMaxLength} characters.")
            : text;
    }
}
