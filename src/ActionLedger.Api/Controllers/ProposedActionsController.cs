using ActionLedger.Application.Review;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ActionLedger.Api.Controllers;

/// <summary>
/// AD-13 — <c>proposed-actions/{id}/decision</c>. The route carries no <c>api/v1</c>:
/// <c>ApiRoutePrefixConvention</c> prepends it, so a controller cannot forget the prefix. AD-2 —
/// the action validates the HTTP shape and calls exactly one handler.
/// </summary>
/// <remarks>
/// <para>
/// A decision is the only way a Tracked Action comes into existence (AD-3). There is no
/// <c>POST /tracked-actions</c>, here or anywhere, and <c>DecisionEndpointTests</c> walks the
/// published contract to keep it that way.
/// </para>
/// <para>
/// AD-12 — deciding needs ActionOfficer or Lead. Both enum members are writers, so a bare
/// <c>[Authorize]</c> is the whole rule, for the reason <c>MeetingsController</c> gives.
/// </para>
/// </remarks>
[ApiController]
[Route("proposed-actions")]
[Authorize]
[Tags("Review")]
public sealed class ProposedActionsController(DecideProposalHandler decide) : ControllerBase
{
    /// <summary>Approves, edits and approves, or rejects a proposed action. Once.</summary>
    /// <remarks>
    /// <para>
    /// The client sends a verb, <c>Approve</c> or <c>Reject</c>, and never the kind: an Approve is
    /// recorded as <c>Approved</c> when the description, due date and owner equal the proposal's —
    /// the owner compared with the server's pre-selected <c>suggestedOwnerUserId</c> — and as
    /// <c>Edited</c> otherwise. The Tracked Action's owner is exactly the <c>ownerUserId</c> sent,
    /// <c>null</c> meaning Unassigned.
    /// </para>
    /// <para>
    /// The decider is the token's <c>sub</c> claim and the instant is the server's clock; neither
    /// can be sent. A proposal is decided once: a second decision, or the loser of two sent at
    /// once, is 409.
    /// </para>
    /// </remarks>
    /// <param name="id">The proposal's id.</param>
    /// <param name="command">The verb and the values it applies to.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <response code="200">The recorded decision, with the new Tracked Action's id unless it was a rejection.</response>
    /// <response code="400">
    /// A missing or unknown verb, a malformed id, a blank or overlong description on an Approve, a
    /// reason on an Approve, an owner who is not on the roster, or an overlong rejection reason.
    /// </response>
    /// <response code="401">No token, or a token this Api did not issue.</response>
    /// <response code="404">No proposal has that id.</response>
    /// <response code="409">The proposal was already decided, or another decision on it committed first.</response>
    [HttpPost("{id}/decision")]
    [EndpointName("DecideProposedAction")]
    [EndpointSummary("Approves, edits and approves, or rejects a proposed action. Once.")]
    [EndpointDescription(
        "Send decision Approve or Reject; the server records Approved or Edited by diffing the "
        + "description, due date and owner against the proposal and its pre-selected owner. The "
        + "Tracked Action's owner is exactly the ownerUserId sent. reason applies to Reject only. "
        + "The decider comes from the sub claim. A proposal that is no longer Pending is 409.")]
    [ProducesResponseType<ProposalDecisionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<ProposalDecisionDto>> Decide(
        [FromRoute] Guid id,
        [FromBody] DecideProposalCommand command,
        CancellationToken cancellationToken) =>
        Ok(await decide.HandleAsync(id, command, cancellationToken));
}
