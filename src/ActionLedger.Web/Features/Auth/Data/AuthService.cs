using ActionLedger.Web.Core.Api;

namespace ActionLedger.Web.Features.Auth.Data;

/// <summary>
/// AD-14 — the <c>Features/&lt;Feature&gt;/Data/&lt;Feature&gt;Service.cs</c> exemplar: a thin
/// wrapper over the generated client, and the only place the Auth feature reaches the API.
/// </summary>
/// <remarks>
/// It exists in a scaffold story because the contract guard is otherwise vacuous. The generated
/// client is git-ignored and regenerated from the committed <c>openapi.json</c>; with no consumer
/// it is unreferenced code and an operation removed from the contract compiles fine. One real
/// caller is what turns "a contract change that was not re-exported fails the build" into a fact.
/// Story 1.6 consumes this rather than replacing it — the login screen, session state, the
/// delegating handler, and the 401 redirect are all its work, not this story's.
/// </remarks>
public sealed class AuthService(IActionLedgerApiClient client)
{
    public Task<SignInResult> SignInAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default) =>
        client.SignInAsync(new SignInCommand { Username = username, Password = password }, cancellationToken);
}
