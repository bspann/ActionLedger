using ActionLedger.Web.Core.Api;
using ActionLedger.Web.Core.Auth;
using ActionLedger.Web.Core.Errors;
using ActionLedger.Web.Core.Users;

namespace ActionLedger.Web.Features.Auth.Data;

/// <summary>
/// AD-14 — the <c>Features/&lt;Feature&gt;/Data/&lt;Feature&gt;Service.cs</c> exemplar: a thin
/// wrapper over the generated client, and the only place the Auth feature reaches the API.
/// </summary>
/// <remarks>
/// It also maps on the way out, which is what makes the login page possible at all.
/// <c>SignInResult</c>, <c>Role</c>, and <c>ApiException</c> are all
/// <c>ActionLedger.Web.Core.Api</c> types and <c>Features.Auth</c> — where <c>LoginPage</c> lives
/// — is outside the seam allowlist <c>WebStructureTests</c> enforces. So the result becomes a
/// <see cref="SignInOutcome"/>, every exception the client throws becomes an
/// <see cref="ApiFailure"/>, and nothing generated escapes this file.
/// </remarks>
public sealed class AuthService(IActionLedgerApiClient client)
{
    public async Task<SignInOutcome> SignInAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        try
        {
            SignInResult result = await client
                .SignInAsync(new SignInCommand { Username = username, Password = password }, cancellationToken)
                .ConfigureAwait(false);

            return SignInOutcome.Succeeded(new SignedInUser(
                result.Token,
                result.ExpiresAt,
                result.User.Id,
                result.User.DisplayName,
                RoleNames.Display(result.User.Role)));
        }
        catch (ApiException exception)
        {
            // Covers all three response shapes: ApiException<ValidationProblemDetails> on 400,
            // ApiException<ProblemDetails> on 401, and a plain ApiException on anything else.
            return SignInOutcome.Failed(ApiFailures.From(exception));
        }
        catch (HttpRequestException exception)
        {
            // A transport failure never becomes an ApiException, so it needs its own arm.
            return SignInOutcome.Failed(ApiFailures.From(exception));
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            // A timed-out POST throws TaskCanceledException, which is neither of the above, so
            // without this arm it escapes as an unhandled component exception instead of the
            // load-failure notice the matrix requires. The filter is what keeps a cancellation
            // the caller actually asked for propagating: that is not a failure to render.
            return SignInOutcome.Failed(ApiFailures.From(exception));
        }
    }
}
