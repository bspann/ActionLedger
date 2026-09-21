using ActionLedger.Application.Abstractions;
using ActionLedger.Application.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ActionLedger.Api.Controllers;

/// <summary>
/// AD-12 — reads are open to any authenticated User, and <c>[Authorize]</c> is an explicit
/// attribute rather than a fallback policy, so the generated contract can say which operations
/// need a token. AD-2 — the controller validates the HTTP shape and calls exactly one query.
/// </summary>
[ApiController]
[Route("users")]
[Authorize]
[Tags("Users")]
public sealed class UsersController(UsersQueries users) : ControllerBase
{
    /// <summary>Lists the people who can sign in.</summary>
    /// <remarks>
    /// System identities are excluded: the <c>Seed</c> User owns seeded rows but is not someone a
    /// Meeting or an action can be assigned to. The roster is ordered by display name.
    /// </remarks>
    /// <param name="page">The 1-based page. Values below 1 are clamped to 1.</param>
    /// <param name="pageSize">Items per page. Values above 200 are clamped to 200.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <response code="200">The requested page of the roster.</response>
    /// <response code="400">A non-numeric <c>page</c> or <c>pageSize</c>.</response>
    /// <response code="401">No token, or a token this Api did not issue.</response>
    [HttpGet]
    [EndpointName("ListUsers")]
    [EndpointSummary("Lists the people who can sign in.")]
    [EndpointDescription(
        "Excludes system identities: the Seed User owns seeded rows but is not someone work can "
        + "be assigned to. Ordered by display name, and paged by the shared page/pageSize window.")]
    [ProducesResponseType<PagedResult<UserSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<ActionResult<PagedResult<UserSummaryDto>>> List(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken) =>
        Ok(await users.ListAsync(page, pageSize, cancellationToken));
}
