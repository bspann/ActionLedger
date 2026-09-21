using ActionLedger.Api.Errors;
using ActionLedger.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

// ControllerBase already has a SignInResult — MVC's, from ControllerBase.SignIn(). The one this
// controller returns is the Application ring's.
using SignInResult = ActionLedger.Application.Auth.SignInResult;

namespace ActionLedger.Api.Controllers;

/// <summary>
/// AD-12 — where a token comes from. The route carries no <c>api/v1</c>:
/// <c>ApiRoutePrefixConvention</c> prepends it, so a controller cannot forget the prefix (AD-13).
/// </summary>
[ApiController]
[Route("auth")]
[AllowAnonymous]
[Tags("Auth")]
public sealed class AuthController(SignInHandler signIn) : ControllerBase
{
    /// <summary>
    /// The one sentence every failed sign-in answers with. It names neither half of the
    /// credential, and it is the same string for an unknown username, a wrong password, and the
    /// system identity — so the response body cannot be used to enumerate accounts.
    /// </summary>
    internal const string RefusedDetail = "The username or password is incorrect.";

    /// <summary>Signs in and returns a bearer token.</summary>
    /// <remarks>
    /// The token is HS256, lasts 8 hours, and carries the caller's id, display name, and role.
    /// Send it back as <c>Authorization: Bearer {token}</c>.
    /// </remarks>
    /// <param name="command">The username and password, as typed.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <response code="200">The token, when it expires, and who the caller now is.</response>
    /// <response code="400">A username or password was missing.</response>
    /// <response code="401">The credentials were refused. The body never says which half was wrong.</response>
    [HttpPost("login")]
    [EndpointName("SignIn")]
    [EndpointSummary("Signs in and returns a bearer token.")]
    [EndpointDescription(
        "The token is HS256, lasts 8 hours, and carries the caller's id, display name, and role. "
        + "A refusal is 401 with a detail that never says which half of the credential was wrong.")]
    [ProducesResponseType<SignInResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<IActionResult> Login(
        [FromBody] SignInCommand command,
        CancellationToken cancellationToken)
    {
        SignInResult? result = await signIn.HandleAsync(command, cancellationToken);

        if (result is null)
        {
            // Not an exception. ApiExceptionHandler maps three exception types and turns anything
            // else into a bare 500, so a custom InvalidCredentials exception would be a 500 here.
            ObjectResult refused = Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                detail: RefusedDetail);

            // The `type`, `title`, `instance`, and correlation id are stamped by
            // ProblemDetailsMapping.Decorate; this pins the media type the way the model-state
            // 400 does, so every error on this route leaves as application/problem+json.
            refused.ContentTypes.Add("application/problem+json");

            return refused;
        }

        return Ok(result);
    }
}
