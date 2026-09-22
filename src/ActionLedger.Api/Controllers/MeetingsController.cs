using ActionLedger.Application.Abstractions;
using ActionLedger.Application.Meetings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ActionLedger.Api.Controllers;

/// <summary>
/// AD-13 — the four Meeting operations. The route carries no <c>api/v1</c>:
/// <c>ApiRoutePrefixConvention</c> prepends it, so a controller cannot forget the prefix. AD-2 —
/// each action validates the HTTP shape and calls exactly one handler or query.
/// </summary>
/// <remarks>
/// <para>
/// ADR-002 — there is no <c>PATCH</c> and no <c>DELETE</c> here, and there never will be. Notes
/// attach once; changing them means creating another Meeting. <c>NotesImmutabilityTests</c> walks
/// the published contract and fails if an operation that could mutate them ever appears.
/// </para>
/// <para>
/// AD-12 — reads are open to any authenticated User and writes need ActionOfficer or Lead. Both
/// enum members are writers, so a bare <c>[Authorize]</c> is the whole rule; a role filter naming
/// every role would read as a restriction while restricting nothing.
/// </para>
/// </remarks>
[ApiController]
[Route("meetings")]
[Authorize]
[Tags("Meetings")]
public sealed class MeetingsController(
    CreateMeetingHandler createMeeting,
    SaveMeetingNotesHandler saveNotes,
    MeetingsQueries meetings) : ControllerBase
{
    /// <summary>Creates a Meeting.</summary>
    /// <remarks>
    /// The Meeting is attributed to the token's <c>sub</c> claim. There is nowhere in the body to
    /// name a different author, and the response echoes the actor that was recorded.
    /// </remarks>
    /// <param name="command">The title, date, and attendees.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <response code="201">The new Meeting's id and the User it was attributed to.</response>
    /// <response code="400">A blank or overlong title, a missing date, or a bad attendee name.</response>
    /// <response code="401">No token, or a token this Api did not issue.</response>
    /// <response code="409">A Meeting with that title already exists on that date.</response>
    [HttpPost]
    [EndpointName("CreateMeeting")]
    [EndpointSummary("Creates a meeting.")]
    [EndpointDescription(
        "The meeting is attributed to the caller's own token: createdByUserId comes from the sub "
        + "claim and never from the request body. A title and date that match an existing meeting "
        + "are a conflict.")]
    [ProducesResponseType<MeetingCreatedDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<MeetingCreatedDto>> Create(
        [FromBody] CreateMeetingCommand command,
        CancellationToken cancellationToken)
    {
        MeetingCreatedDto created = await createMeeting.HandleAsync(command, cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>Attaches the meeting's notes. Once.</summary>
    /// <remarks>
    /// The text is stored byte-for-byte, with a SHA-256 of exactly those bytes. A second call is
    /// 409: notes cannot be changed after saving, and no endpoint updates or deletes them.
    /// </remarks>
    /// <param name="id">The Meeting's id.</param>
    /// <param name="command">The notes, exactly as pasted.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <response code="200">The saved notes, with their id, hash, and save time.</response>
    /// <response code="400">Empty notes, notes over 50,000 characters, or a malformed id.</response>
    /// <response code="401">No token, or a token this Api did not issue.</response>
    /// <response code="404">No Meeting has that id.</response>
    /// <response code="409">The Meeting already has notes.</response>
    [HttpPut("{id}/notes")]
    [EndpointName("SaveMeetingNotes")]
    [EndpointSummary("Attaches the meeting's notes. Once.")]
    [EndpointDescription(
        "Notes are stored byte-for-byte with a SHA-256 of exactly those bytes. They cannot be "
        + "changed after saving: a second call is 409, and no endpoint updates or deletes them.")]
    [ProducesResponseType<MeetingNotesDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<MeetingNotesDto>> SaveNotes(
        [FromRoute] Guid id,
        [FromBody] SaveMeetingNotesCommand command,
        CancellationToken cancellationToken) =>
        Ok(await saveNotes.HandleAsync(id, command, cancellationToken));

    /// <summary>Lists meetings, newest first.</summary>
    /// <remarks>
    /// Ordered by meeting date descending, then by creation time, then by id — a total order, so
    /// two rows never tie and paging over an unchanging set cannot return a row twice or skip one.
    /// The count and the window are separate statements, so a meeting created between them can
    /// still shift a row across a page boundary.
    /// </remarks>
    /// <param name="page">The 1-based page. Values below 1 are clamped to 1.</param>
    /// <param name="pageSize">Items per page. Values above 200 are clamped to 200.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <response code="200">The requested page of meetings.</response>
    /// <response code="400">A non-numeric <c>page</c> or <c>pageSize</c>.</response>
    /// <response code="401">No token, or a token this Api did not issue.</response>
    [HttpGet]
    [EndpointName("ListMeetings")]
    [EndpointSummary("Lists meetings, newest first.")]
    [EndpointDescription(
        "Ordered by meeting date descending, then creation time, then id, and paged by the shared "
        + "page/pageSize window. runCount and trackedActionCount are published now and are 0 until "
        + "extraction runs and tracked actions exist.")]
    [ProducesResponseType<PagedResult<MeetingSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<ActionResult<PagedResult<MeetingSummaryDto>>> List(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken) =>
        Ok(await meetings.ListAsync(page, pageSize, cancellationToken));

    /// <summary>Reads one meeting, with its notes.</summary>
    /// <param name="id">The Meeting's id.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <response code="200">The meeting. <c>notes</c> is null while none have been attached.</response>
    /// <response code="400">A malformed id.</response>
    /// <response code="401">No token, or a token this Api did not issue.</response>
    /// <response code="404">No Meeting has that id.</response>
    [HttpGet("{id}")]
    [EndpointName("GetMeeting")]
    [EndpointSummary("Reads one meeting, with its notes.")]
    [EndpointDescription(
        "notes is null while none have been attached, and otherwise carries the text exactly as it "
        + "was pasted together with the SHA-256 of those bytes.")]
    [ProducesResponseType<MeetingDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<MeetingDetailDto>> Get(
        [FromRoute] Guid id,
        CancellationToken cancellationToken) =>
        Ok(await meetings.GetAsync(id, cancellationToken));
}
