using System.Net;
using System.Net.Http.Headers;
using ActionLedger.Web.Core.Shell;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace ActionLedger.Web.Core.Auth;

/// <summary>
/// UX-DR18 and UX-DR19 — the one place the global response behaviour lives. It brackets every
/// send with <see cref="LoadingState"/>, attaches the bearer token when there is a session, and
/// turns 401, 403, and 409 into the behaviour EXPERIENCE.md specifies.
/// </summary>
/// <remarks>
/// <para>
/// The 401 branch is gated on "this request carried a token". The login POST is anonymous and
/// answers 401 on a bad password; treating every 401 alike would snackbar "Session expired. Sign
/// in again." at a user who simply mistyped, bounce them to the page they are already on, and
/// make UX-DR18's inline message unreachable. "Carried a token" is exactly the condition that
/// separates an expired session from a refused credential.
/// </para>
/// <para>
/// The handler is chained by hand in <c>ApiClientRegistration</c> rather than through
/// <c>AddHttpClient</c>: <c>Microsoft.Extensions.Http</c> is not a pinned package and NFR9 keeps
/// the list short.
/// </para>
/// </remarks>
public sealed class SessionMessageHandler(
    SessionState session,
    LoadingState loading,
    ISnackbar snackbar,
    NavigationManager navigation) : DelegatingHandler
{
    /// <summary>Where a 401 on an authenticated request sends the user.</summary>
    public const string LoginRoute = "/login";

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        bool carriedToken = false;

        if (session.Token is { Length: > 0 } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            carriedToken = true;
        }

        HttpResponseMessage response;

        loading.Begin();

        try
        {
            response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // A transport failure or a cancellation throws out of SendAsync. Without this the
            // counter never comes back down and the progress bar stays up for the whole session.
            loading.End();
        }

        switch (response.StatusCode)
        {
            case HttpStatusCode.Unauthorized when carriedToken:
                ExpireSession();
                break;

            case HttpStatusCode.Forbidden:
                snackbar.Add(Voice.RoleNotAllowed, Severity.Warning);
                break;

            case HttpStatusCode.Conflict:
                // No Retry is offered: the same request can never succeed. Refreshing the record
                // belongs to the calling page, and there is no such page until Epic 2.
                snackbar.Add(Voice.AlreadyChanged, Severity.Warning);
                break;

            default:
                break;
        }

        return response;
    }

    private void ExpireSession()
    {
        // Expiring twice is a real sequence, not a theoretical one: two authenticated requests in
        // flight together both answer 401, or one lands after the user has already signed out
        // from the menu. A second pass would raise a second snackbar, push a second redirect, and
        // — because SignOut clears the attempted route — throw away the route the first pass
        // captured, so restore-once would silently restore nothing.
        if (!session.IsSignedIn)
        {
            return;
        }

        string attempted = CurrentRoute();

        session.SignOut();

        // Captured after SignOut, which clears any earlier route, so exactly one attempted route
        // survives and SessionState.TakeAttemptedRoute hands it back once. The comparison is on
        // the path alone — "/login?returnUrl=x" is still the login page, and capturing it would
        // send the user straight back to the form they just came from — while the route that is
        // captured keeps its query, because a filtered list lives in its query string.
        if (PathOf(attempted) is not LoginRoute and not "/")
        {
            session.CaptureAttemptedRoute(attempted);
        }

        snackbar.Add(Voice.SessionExpired, Severity.Warning);
        navigation.NavigateTo(LoginRoute);
    }

    private string CurrentRoute() => "/" + navigation.ToBaseRelativePath(navigation.Uri);

    private static string PathOf(string route)
    {
        int cut = route.IndexOfAny(['?', '#']);

        return cut < 0 ? route : route[..cut];
    }
}
