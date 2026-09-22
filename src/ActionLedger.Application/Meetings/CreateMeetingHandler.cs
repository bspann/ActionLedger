using System.ComponentModel.DataAnnotations;
using ActionLedger.Application.Abstractions;
using ActionLedger.Domain.Common;
using ActionLedger.Domain.Meetings;

namespace ActionLedger.Application.Meetings;

/// <summary>
/// AD-2 — one <c>&lt;Verb&gt;&lt;Noun&gt;Handler</c> per write use case: one class, one
/// <c>HandleAsync</c>, ports for everything it cannot do itself. AD-20 — exactly one
/// <see cref="IUnitOfWork.CommitAsync"/>, called here and nowhere below.
/// </summary>
/// <remarks>
/// AD-12 — the actor comes from <see cref="ICurrentUser"/>, which reads the token's <c>sub</c>
/// claim. <see cref="CreateMeetingCommand"/> deliberately has nowhere to put one, so a caller
/// cannot create a Meeting attributed to somebody else even by accident.
/// </remarks>
public sealed class CreateMeetingHandler(
    IMeetingRepository meetings,
    ICurrentUser currentUser,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    /// <summary>Creates a Meeting and returns its id and the actor the server stamped on it.</summary>
    /// <exception cref="DomainRuleException">A field is blank or too long.</exception>
    /// <exception cref="ConcurrencyConflictException">
    /// A Meeting with that title already exists on that date — the AD-20
    /// <c>meeting(title, meeting_date)</c> unique index.
    /// </exception>
    public async Task<MeetingCreatedDto> HandleAsync(
        CreateMeetingCommand command,
        CancellationToken cancellationToken = default)
    {
        // [ApiController] model validation answers a missing date with a 400 long before this
        // runs; the throw is what keeps the handler honest when it is called directly.
        DateOnly meetingDate = command.MeetingDate
            ?? throw new DomainRuleException("A Meeting's date is required.");

        Meeting meeting = Meeting.Create(
            command.Title,
            meetingDate,
            command.Attendees,
            currentUser.UserId,
            clock.UtcNow);

        meetings.Add(meeting);

        await unitOfWork.CommitAsync(cancellationToken);

        return new MeetingCreatedDto(meeting.Id, meeting.CreatedByUserId);
    }
}

/// <summary>
/// The create request. It is also the body <c>POST /api/v1/meetings</c> binds, so the annotations
/// here are what turn a blank title or a missing date into <c>[ApiController]</c>'s 400 rather
/// than into a 409 from a Domain rule.
/// </summary>
/// <remarks>
/// There is no <c>createdByUserId</c> and no member that could stand in for one (AD-12, FR-25).
/// <c>ActorIntegrityTests</c> fails the build if one appears.
/// </remarks>
public sealed record CreateMeetingCommand
{
    /// <summary>What the meeting was called. Trimmed before it is stored.</summary>
    [Required(ErrorMessage = "A title is required.")]
    [StringLength(Meeting.TitleMaxLength, MinimumLength = 1, ErrorMessage = "A title must be between {2} and {1} characters.")]
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// The calendar day the meeting happened. Nullable so an omitted date is a 400 rather than
    /// silently becoming the first day of year one.
    /// </summary>
    [Required(ErrorMessage = "A meeting date is required.")]
    public DateOnly? MeetingDate { get; init; }

    /// <summary>Who was there. Omit it, or send an empty array, for none.</summary>
    [AttendeeNames]
    public string[] Attendees { get; init; } = [];
}

/// <summary>
/// Validates each attendee name, which <c>[StringLength]</c> cannot do: its <c>IsValid</c> casts
/// the value to <c>string</c>, so on a <c>string[]</c> it throws <c>InvalidCastException</c>
/// during model validation rather than measuring anything. Without this, a 101-character attendee
/// would reach the Domain and come back as a 409 where the matrix asks for a 400.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class AttendeeNamesAttribute : ValidationAttribute
{
    /// <summary>The shortest an attendee name may be. Blank is refused, so this is one.</summary>
    /// <remarks>
    /// Published on the contract by <c>OpenApiSetup.DescribeCollectionElementBoundsAsync</c>, which
    /// stamps these two onto the array's <c>items</c> schema. Without that the generated client and
    /// any integrator see a bare array of strings and learn the rule only from a 400.
    /// </remarks>
    public int MinimumLength => 1;

    /// <summary>The longest an attendee name may be, after trimming.</summary>
    public int MaximumLength => Meeting.AttendeeMaxLength;

    /// <inheritdoc />
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not IEnumerable<string> attendees)
        {
            return ValidationResult.Success;
        }

        foreach (string attendee in attendees)
        {
            if (string.IsNullOrWhiteSpace(attendee))
            {
                return new ValidationResult("An attendee name cannot be blank.", [validationContext.MemberName ?? nameof(CreateMeetingCommand.Attendees)]);
            }

            if (attendee.Trim().Length > MaximumLength)
            {
                return new ValidationResult(
                    $"An attendee name cannot exceed {MaximumLength} characters.",
                    [validationContext.MemberName ?? nameof(CreateMeetingCommand.Attendees)]);
            }
        }

        return ValidationResult.Success;
    }
}
