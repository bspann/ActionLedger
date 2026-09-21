using System.ComponentModel.DataAnnotations;
using ActionLedger.Api.Observability;
using ActionLedger.Application.Abstractions;
using ActionLedger.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ActionLedger.Api.Tests;

/// <summary>
/// Routes that exist only inside <c>Api.Tests</c>, added to the host through
/// <c>WebApplicationFactory</c> when <see cref="TestApi.IncludeTestEndpoints"/> is set.
/// </summary>
/// <remarks>
/// The error matrix covers 401, 403, 400, 404, and both flavours of 409. None of those has a
/// production route until Story 1.4, and shipping placeholder endpoints to make the tests
/// possible would put dead routes in the contract. Declaring them here proves the mapping
/// against the real middleware pipeline while the committed contract stays empty of them —
/// <see cref="OpenApiSnapshotTest"/> runs a host that does not include this assembly.
/// </remarks>
[ApiController]
[Route("errors")]
public sealed class ErrorProbeController : ControllerBase
{
    [HttpGet("domain-rule")]
    [AllowAnonymous]
    public IActionResult DomainRule() => throw new DomainRuleException("A Tracked Action cannot move from Completed to Pending.");

    [HttpGet("concurrency")]
    [AllowAnonymous]
    public IActionResult Concurrency() => throw new ConcurrencyConflictException("The Tracked Action changed while you were editing it.");

    [HttpGet("not-found")]
    [AllowAnonymous]
    public IActionResult Missing() => throw new NotFoundException("Meeting", Guid.Empty);

    [HttpGet("unexpected")]
    [AllowAnonymous]
    public IActionResult Unexpected() => throw new InvalidOperationException("Connection string leaked through an unmapped exception.");

    [HttpPost("validate")]
    [AllowAnonymous]
    public IActionResult Validate([FromBody] ProbePayload payload) => Ok(payload);

    [HttpGet("correlation")]
    [AllowAnonymous]
    public IActionResult Correlation() => Ok(new { correlationId = RequestCorrelation.For(HttpContext) });

    /// <summary>A body whose annotations make [ApiController]'s automatic 400 fire.</summary>
    public sealed record ProbePayload
    {
        [Required(ErrorMessage = "A description is required.")]
        [MinLength(3, ErrorMessage = "A description needs at least 3 characters.")]
        public string? Description { get; init; }

        [Range(1, 5, ErrorMessage = "Priority runs from 1 to 5.")]
        public int Priority { get; init; }
    }
}

/// <summary>Routes that exercise the authentication and authorization mapping.</summary>
[ApiController]
[Route("protected")]
[Authorize]
public sealed class AuthProbeController : ControllerBase
{
    /// <summary>Any authenticated User. No token is a 401.</summary>
    [HttpGet("any")]
    public IActionResult AnyRole() => Ok(new { user = User.Identity?.Name });

    /// <summary>Lead only. An authenticated Action Officer is a 403 (AD-12's Cancelled transition shape).</summary>
    [HttpGet("lead-only")]
    [Authorize(Roles = "Lead")]
    public IActionResult LeadOnly() => Ok(new { user = User.Identity?.Name });

    /// <summary>
    /// Echoes the actor the container resolved for this request. AD-12's <c>ICurrentUser</c> has
    /// no production caller until the first write use case, so without a route that resolves it
    /// through DI its registration and its lifetime would both be unexercised — and the lifetime
    /// is the behaviour, because the class snapshots the claims at construction.
    /// </summary>
    [HttpGet("current-user")]
    public IActionResult CurrentActor([FromServices] ICurrentUser current) =>
        Ok(new { current.UserId, current.DisplayName, Role = current.Role.ToString() });
}
