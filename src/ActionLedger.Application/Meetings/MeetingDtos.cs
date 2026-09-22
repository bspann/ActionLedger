namespace ActionLedger.Application.Meetings;

/// <summary>
/// AD-13 — a Meeting as the Meeting List renders it.
/// </summary>
/// <remarks>
/// <see cref="RunCount"/> is a correlated count of this Meeting's extraction runs as of Story 2.5.
/// <see cref="TrackedActionCount"/> is still projected as <c>0</c>, because the table behind it
/// does not exist yet: tracked actions arrive with Story 3.1. The envelope was fixed before either
/// had a source — the web client is generated from the committed contract, so publishing both
/// fields up front meant each story changes one <c>Select</c> rather than reshaping the generated
/// client twice.
/// </remarks>
/// <param name="Id">The Meeting's id.</param>
/// <param name="Title">The meeting's title.</param>
/// <param name="MeetingDate">The calendar day the meeting happened.</param>
/// <param name="RunCount">How many extraction runs this Meeting has, Succeeded and Failed alike.</param>
/// <param name="TrackedActionCount">How many tracked actions came out of it. Always <c>0</c> until Story 3.1.</param>
public sealed record MeetingSummaryDto(
    Guid Id,
    string Title,
    DateOnly MeetingDate,
    int RunCount,
    int TrackedActionCount);

/// <summary>AD-13 — a Meeting as the Meeting Detail screen reads it, notes included.</summary>
/// <param name="Id">The Meeting's id.</param>
/// <param name="Title">The meeting's title.</param>
/// <param name="MeetingDate">The calendar day the meeting happened.</param>
/// <param name="Attendees">Who was there. Empty rather than null when nobody was recorded.</param>
/// <param name="CreatedByUserId">The User who created it, from the token's <c>sub</c> claim (AD-12).</param>
/// <param name="CreatedAt">When it was created, in UTC.</param>
/// <param name="Notes">The pasted notes, or <c>null</c> while none have been attached.</param>
public sealed record MeetingDetailDto(
    Guid Id,
    string Title,
    DateOnly MeetingDate,
    IReadOnlyList<string> Attendees,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt,
    MeetingNotesDto? Notes);

/// <summary>
/// AD-13 — the notes as they were pasted, with the hash that makes the stored text checkable.
/// There is no shape for changing them, here or anywhere else (ADR-002).
/// </summary>
/// <param name="Id">The notes' id.</param>
/// <param name="Text">The notes, byte-for-byte as they were submitted.</param>
/// <param name="Sha256">Lower-case hex SHA-256 of the UTF-8 bytes of <paramref name="Text"/>.</param>
/// <param name="SavedAt">When the notes were saved, in UTC.</param>
public sealed record MeetingNotesDto(Guid Id, string Text, string Sha256, DateTimeOffset SavedAt);

/// <summary>
/// What <c>POST /api/v1/meetings</c> answers with: the new id, and the actor the server stamped.
/// </summary>
/// <remarks>
/// The actor is echoed back deliberately. AD-12 takes it from the token rather than from the
/// request, so returning it is how a caller finds out what was recorded rather than what they
/// might have hoped to supply.
/// </remarks>
/// <param name="Id">The new Meeting's id.</param>
/// <param name="CreatedByUserId">The User the server attributed it to — the token's <c>sub</c>.</param>
public sealed record MeetingCreatedDto(Guid Id, Guid CreatedByUserId);
