namespace ActionLedger.Application.Ai;

/// <summary>
/// AD-11 — everything a provider is given about a Meeting, and nothing else.
/// </summary>
/// <remarks>
/// The v1 prompt's <c>## Input</c> section names exactly these two values, and PRD FR-4 says no
/// other Meeting field is sent: not the title, not the attendees, not the id. A provider that can
/// name the meeting cannot be shown to have read only the notes, and NFR-4 keeps notes text out of
/// logs for the same reason.
/// </remarks>
/// <param name="Notes">The notes, byte-for-byte as they were saved. Nothing normalizes this.</param>
/// <param name="MeetingDate">The date the meeting took place. Relative dates resolve against it.</param>
public sealed record ExtractionRequest(string Notes, DateOnly MeetingDate);
