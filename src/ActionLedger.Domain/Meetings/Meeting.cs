using ActionLedger.Domain.Common;

namespace ActionLedger.Domain.Meetings;

/// <summary>
/// AD-3 — the aggregate an Action Officer creates before anything can be extracted. It owns
/// exactly one child, <see cref="MeetingNotes"/>, and owns it write-once: extraction runs,
/// proposals, and tracked actions are separate roots that arrive with their own stories.
/// </summary>
/// <remarks>
/// <para>
/// There is no public constructor and no public setter. A Meeting comes into existence through
/// <see cref="Create"/>, and the only state transition it has is <see cref="AttachNotes"/> —
/// which can happen once (AD-5, ADR-002). There is deliberately no method that updates or clears
/// notes: to change what was pasted, a user creates another Meeting.
/// </para>
/// <para>
/// The actor is a parameter rather than something this type reads, because Domain has no way to
/// see a token. AD-12 puts the <c>sub</c> claim into <see cref="CreatedByUserId"/> at the handler.
/// </para>
/// </remarks>
public sealed class Meeting : AggregateRoot
{
    /// <summary>The longest title the <c>meeting(title, meeting_date)</c> unique index has to carry.</summary>
    public const int TitleMaxLength = 200;

    /// <summary>The longest single attendee name. Attendees are names, not free text.</summary>
    public const int AttendeeMaxLength = 100;

    private Meeting(
        string title,
        DateOnly meetingDate,
        IReadOnlyList<string> attendees,
        Guid createdByUserId,
        DateTimeOffset createdAt)
    {
        Title = title;
        MeetingDate = meetingDate;
        Attendees = attendees;
        CreatedByUserId = createdByUserId;
        CreatedAt = createdAt;
    }

    /// <summary>Rehydration constructor for EF Core.</summary>
    private Meeting()
    {
        Title = string.Empty;
        Attendees = [];
    }

    /// <summary>
    /// What the meeting was called, trimmed. Trimming matters: <c>meeting(title, meeting_date)</c>
    /// is unique, and "Standup" and "Standup " would otherwise be two different meetings.
    /// </summary>
    public string Title { get; private set; }

    /// <summary>The calendar day the meeting happened, stored as a <c>date</c> (AD-10).</summary>
    public DateOnly MeetingDate { get; private set; }

    /// <summary>
    /// Who was there, each name trimmed. May be empty; a meeting with no recorded attendees is
    /// ordinary, and the roster is not what extraction reads.
    /// </summary>
    public IReadOnlyList<string> Attendees { get; private set; }

    /// <summary>
    /// The User who created this Meeting, from the token's <c>sub</c> claim (AD-12). Never a value
    /// a request body supplied.
    /// </summary>
    public Guid CreatedByUserId { get; private set; }

    /// <summary>When the Meeting was created, from <c>IClock</c> — never <c>DateTimeOffset.UtcNow</c> (AD-15).</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// The notes pasted into this Meeting, or <c>null</c> while none have been. Once set, it is
    /// never replaced and never cleared.
    /// </summary>
    public MeetingNotes? Notes { get; private set; }

    /// <summary>Whether notes have already been attached. The only thing callers need to ask.</summary>
    public bool HasNotes => Notes is not null;

    /// <summary>Creates a Meeting.</summary>
    /// <param name="title">The meeting's title; trimmed.</param>
    /// <param name="meetingDate">The calendar day the meeting happened.</param>
    /// <param name="attendees">Who was there; each name trimmed. <c>null</c> means none.</param>
    /// <param name="createdByUserId">The acting User's id, from <c>ICurrentUser</c> (AD-12).</param>
    /// <param name="createdAt">The creation instant, from <c>IClock</c>.</param>
    /// <exception cref="DomainRuleException">A required field is blank, too long, or missing.</exception>
    public static Meeting Create(
        string title,
        DateOnly meetingDate,
        IEnumerable<string>? attendees,
        Guid createdByUserId,
        DateTimeOffset createdAt) =>
        new(
            RequireText(title, "title", TitleMaxLength),
            meetingDate,
            NormalizeAttendees(attendees),
            RequireActor(createdByUserId),
            createdAt.ToUniversalTime());

    /// <summary>
    /// Attaches the pasted notes. This is the write-once transition AD-5 and ADR-002 describe:
    /// calling it a second time is a rule violation, not an update.
    /// </summary>
    /// <param name="text">The notes exactly as submitted. Stored byte-for-byte; never trimmed.</param>
    /// <param name="savedAt">The instant the notes were saved, from <c>IClock</c>.</param>
    /// <returns>The notes that were attached, so a caller can report their id and hash.</returns>
    /// <exception cref="DomainRuleException">
    /// This Meeting already has notes, or the text is empty or too long.
    /// </exception>
    public MeetingNotes AttachNotes(string text, DateTimeOffset savedAt)
    {
        if (HasNotes)
        {
            throw new DomainRuleException(
                "This meeting already has notes. Notes cannot be changed after saving; create a new meeting instead.");
        }

        Notes = new MeetingNotes(Id, text, savedAt.ToUniversalTime());

        return Notes;
    }

    private static Guid RequireActor(Guid createdByUserId) =>
        createdByUserId == Guid.Empty
            ? throw new DomainRuleException("A Meeting must record the User who created it.")
            : createdByUserId;

    private static IReadOnlyList<string> NormalizeAttendees(IEnumerable<string>? attendees) =>
        attendees is null
            ? []
            : [.. attendees.Select(attendee => RequireText(attendee, "attendee name", AttendeeMaxLength))];

    private static string RequireText(string value, string field, int maxLength)
    {
        string trimmed = (value ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            throw new DomainRuleException($"A Meeting's {field} is required.");
        }

        return trimmed.Length > maxLength
            ? throw new DomainRuleException($"A Meeting's {field} cannot exceed {maxLength} characters.")
            : trimmed;
    }
}
