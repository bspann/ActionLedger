using ActionLedger.Application.Extraction;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ActionLedger.Api.Controllers;

/// <summary>
/// AD-13 — <c>runs/{id}</c>. The route carries no <c>api/v1</c>:
/// <c>ApiRoutePrefixConvention</c> prepends it, so a controller cannot forget the prefix. AD-2 —
/// the action validates the HTTP shape and calls exactly one query.
/// </summary>
/// <remarks>
/// <para>
/// A run is read here and created under <c>meetings/{id}/runs</c>, because creating one is
/// something you do to a Meeting and reading one is not. That split is AD-13's, and it is what the
/// 201's <c>Location</c> crosses.
/// </para>
/// <para>
/// There is no <c>POST</c>, <c>PUT</c>, <c>PATCH</c> or <c>DELETE</c> here, and there will not be.
/// A run records what happened; to get a different answer a user starts another run (AD-5).
/// </para>
/// <para>
/// AD-12 — reads are open to any authenticated User, so a bare <c>[Authorize]</c> is the whole
/// rule.
/// </para>
/// </remarks>
[ApiController]
[Route("runs")]
[Authorize]
[Tags("Runs")]
public sealed class RunsController(RunsQueries runs) : ControllerBase
{
    /// <summary>Reads one extraction run, with its metadata and proposals.</summary>
    /// <remarks>
    /// Every FR-6 field is present whatever the outcome: token counts render as <c>0</c> rather
    /// than blank, and <c>warnings</c> is an array that is empty rather than absent. The proposals
    /// come back in the order the AI returned them, each carrying the server-computed
    /// <c>isLowConfidence</c> and <c>suggestedOwnerUserId</c> — the client recomputes neither
    /// (AD-15).
    /// </remarks>
    /// <param name="id">The run's id.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <response code="200">The run, its metadata, its warnings, and its proposals in AI order.</response>
    /// <response code="400">A malformed id.</response>
    /// <response code="401">No token, or a token this Api did not issue.</response>
    /// <response code="404">No run has that id.</response>
    [HttpGet("{id}")]
    [EndpointName("GetExtractionRun")]
    [EndpointSummary("Reads one extraction run, with its metadata and proposals.")]
    [EndpointDescription(
        "Carries provider, model, prompt version, schema version, start time, duration, both "
        + "token counts, outcome, failure reason and warnings. Proposals come back in AI order "
        + "with isLowConfidence and suggestedOwnerUserId already computed server-side.")]
    [ProducesResponseType<RunDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<RunDetailDto>> Get(
        [FromRoute] Guid id,
        CancellationToken cancellationToken) =>
        Ok(await runs.GetAsync(id, cancellationToken));
}
