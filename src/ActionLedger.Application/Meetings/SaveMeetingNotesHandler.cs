using System.ComponentModel.DataAnnotations;
using ActionLedger.Application.Abstractions;
using ActionLedger.Domain.Common;
using ActionLedger.Domain.Meetings;

namespace ActionLedger.Application.Meetings;

/// <summary>
/// FR-2 — notes attach to a Meeting exactly once. This handler is the only write path to them,
/// and there is no sibling that updates or deletes them (ADR-002).
/// </summary>
/// <remarks>
/// <para>
/// The two failures are kept distinct on purpose: a Meeting that is not there is a
/// <see cref="NotFoundException"/> (404), and a Meeting that already has notes is a
/// <see cref="DomainRuleException"/> (409). The <c>meeting_notes(meeting_id)</c> unique index
/// answers the concurrent case with the *same* 409 through
/// <see cref="ConcurrencyConflictException"/>, so there is no third code path to keep in step.
/// </para>
/// <para>
/// AD-20 — one <see cref="IUnitOfWork.CommitAsync"/>, here.
/// </para>
/// </remarks>
public sealed class SaveMeetingNotesHandler(
    IMeetingRepository meetings,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    /// <summary>Attaches the pasted notes to a Meeting, once.</summary>
    /// <param name="meetingId">The Meeting from the route.</param>
    /// <param name="command">The notes, exactly as submitted.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <exception cref="NotFoundException">No Meeting has that id.</exception>
    /// <exception cref="DomainRuleException">The Meeting already has notes.</exception>
    /// <exception cref="ConcurrencyConflictException">Another writer attached notes first.</exception>
    public async Task<MeetingNotesDto> HandleAsync(
        Guid meetingId,
        SaveMeetingNotesCommand command,
        CancellationToken cancellationToken = default)
    {
        Meeting meeting = await meetings.FindByIdAsync(meetingId, cancellationToken)
            ?? throw new NotFoundException("Meeting", meetingId);

        MeetingNotes notes = meeting.AttachNotes(command.Text, clock.UtcNow);

        await unitOfWork.CommitAsync(cancellationToken);

        return new MeetingNotesDto(notes.Id, notes.Text, notes.Sha256, notes.SavedAt);
    }
}

/// <summary>
/// The notes to attach. It is also the body <c>PUT /api/v1/meetings/{id}/notes</c> binds, so the
/// length rule here is what answers an empty or oversized paste with a 400.
/// </summary>
/// <remarks>
/// There is no <c>replace</c> flag and no notes id: a second save is a conflict, never an update.
/// </remarks>
public sealed record SaveMeetingNotesCommand
{
    /// <summary>
    /// The notes, exactly as typed or pasted. Stored byte-for-byte — whatever whitespace and line
    /// endings arrive here are what a read gives back, and what the SHA-256 is taken over.
    /// </summary>
    [Required(ErrorMessage = "Notes are required.")]
    [StringLength(MeetingNotes.TextMaxLength, MinimumLength = 1, ErrorMessage = "Notes must be between {2} and {1} characters.")]
    public string Text { get; init; } = string.Empty;
}
