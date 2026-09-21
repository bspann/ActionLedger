using System.Net;
using ActionLedger.Web.Core;
using ActionLedger.Web.Core.Auth;
using ActionLedger.Web.Core.Shell;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Xunit;

namespace ActionLedger.Web.Tests;

/// <summary>
/// UX-DR18 and UX-DR19 — the global response behaviour. The bearer token is attached when there
/// is a session and only then; a 401 on a request that carried a token expires the session, and a
/// 401 on one that did not is left alone; 403 and 409 raise their exact snackbars; and the
/// progress counter comes back to zero on every exit, including a thrown send.
/// </summary>
/// <remarks>
/// Driven through a stub <see cref="HttpMessageHandler"/>, so no network is involved. The
/// snackbar and the navigation manager are the real bUnit and MudBlazor doubles, which is what
/// makes "snackbars this exact string" and "navigates to /login" assertable rather than inferred.
/// </remarks>
public sealed class SessionMessageHandlerTests : BunitContext
{
    private readonly SessionState session = new();
    private readonly LoadingState loading = new();
    private readonly StubInnerHandler inner;

    public SessionMessageHandlerTests()
    {
        inner = new StubInnerHandler { Observing = loading };

        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public async Task A_signed_in_session_attaches_the_bearer_token()
    {
        SignIn();

        await SendAsync();

        Assert.NotNull(inner.LastRequest?.Headers.Authorization);
        Assert.Equal("Bearer", inner.LastRequest.Headers.Authorization.Scheme);
        Assert.Equal("jwt", inner.LastRequest.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task With_no_session_no_authorization_header_is_attached()
    {
        // The login POST goes out through this same handler and must stay anonymous.
        await SendAsync();

        Assert.Null(inner.LastRequest?.Headers.Authorization);
    }

    [Fact]
    public async Task A_401_on_a_request_that_carried_a_token_expires_the_session()
    {
        SignIn();
        Navigation.NavigateTo("/actions/7");
        inner.Status = HttpStatusCode.Unauthorized;

        await SendAsync();

        Assert.False(session.IsSignedIn);
        Assert.Equal("/actions/7", session.TakeAttemptedRoute());
        Assert.Contains(Snackbar.ShownSnackbars, shown => shown.Message == Voice.SessionExpired);
        // bUnit records navigations newest-first, so the most recent one is the head.
        Assert.Equal(SessionMessageHandler.LoginRoute, Navigation.History.First().Uri);
    }

    [Fact]
    public async Task A_401_on_a_request_that_carried_no_token_does_nothing_at_all()
    {
        // This is the login refusal. If the handler treated every 401 alike, a mistyped password
        // would snackbar "Session expired. Sign in again." and bounce the user to the page they
        // are already on, and UX-DR18's inline message would never be seen.
        inner.Status = HttpStatusCode.Unauthorized;

        await SendAsync();

        Assert.Empty(Snackbar.ShownSnackbars);
        Assert.Empty(Navigation.History);
        Assert.Null(session.TakeAttemptedRoute());
    }

    [Fact]
    public async Task Two_authenticated_requests_that_both_answer_401_expire_the_session_once()
    {
        // Both attach a token before either response comes back, so both reach the 401 branch.
        // Without a guard the second pass signs out an already-signed-out session, and SignOut
        // clears the attempted route the first pass had just captured — so restore-once restores
        // nothing and the user lands on /meetings instead of where they were going.
        Snackbar.Configuration.PreventDuplicates = false;

        SignIn();
        Navigation.NavigateTo("/actions/7");
        inner.Status = HttpStatusCode.Unauthorized;

        TaskCompletionSource gate = new();
        inner.Gate = gate.Task;

        SessionMessageHandler handler = new(session, loading, Snackbar, Navigation) { InnerHandler = inner };
        using HttpClient client = new(handler) { BaseAddress = new Uri("http://localhost/") };

        Task<HttpResponseMessage> first = client.GetAsync("api/v1/users", Xunit.TestContext.Current.CancellationToken);
        Task<HttpResponseMessage> second = client.GetAsync("api/v1/meetings", Xunit.TestContext.Current.CancellationToken);

        gate.SetResult();
        (await first).Dispose();
        (await second).Dispose();

        Assert.Equal("/actions/7", session.TakeAttemptedRoute());
        Assert.Equal(1, Snackbar.ShownSnackbars.Count(shown => shown.Message == Voice.SessionExpired));
        Assert.Equal(1, Navigation.History.Count(entry => entry.Uri == SessionMessageHandler.LoginRoute));
    }

    [Fact]
    public async Task A_401_landing_after_a_deliberate_sign_out_announces_nothing()
    {
        // The other way the branch runs twice: the request carried a token, but by the time it
        // answers the user has already chosen Sign out. Telling them their session expired, and
        // redirecting them again, is noise about something they just did on purpose.
        SignIn();
        inner.Status = HttpStatusCode.Unauthorized;

        TaskCompletionSource gate = new();
        inner.Gate = gate.Task;

        SessionMessageHandler handler = new(session, loading, Snackbar, Navigation) { InnerHandler = inner };
        using HttpClient client = new(handler) { BaseAddress = new Uri("http://localhost/") };

        Task<HttpResponseMessage> pending = client.GetAsync("api/v1/users", Xunit.TestContext.Current.CancellationToken);

        session.SignOut();

        gate.SetResult();
        (await pending).Dispose();

        Assert.Empty(Snackbar.ShownSnackbars);
        Assert.Empty(Navigation.History);
    }

    [Fact]
    public async Task A_captured_route_keeps_its_query_string()
    {
        // The Action List reflects its filters in the query string so a filtered view can be
        // linked. Restoring the path alone would drop the filters the user was looking at.
        SignIn();
        Navigation.NavigateTo("/actions?status=Open&overdue=true");
        inner.Status = HttpStatusCode.Unauthorized;

        await SendAsync();

        Assert.Equal("/actions?status=Open&overdue=true", session.TakeAttemptedRoute());
    }

    [Theory]
    [InlineData("/login")]
    [InlineData("/login?returnUrl=%2Factions")]
    [InlineData("/")]
    [InlineData("/?x=1")]
    public async Task The_login_page_is_never_captured_as_the_attempted_route(string route)
    {
        // Capturing it would send the user, after signing in, straight back to the form they
        // just came from. A query string does not make the login page a different page.
        SignIn();
        Navigation.NavigateTo(route);
        inner.Status = HttpStatusCode.Unauthorized;

        await SendAsync();

        Assert.Null(session.TakeAttemptedRoute());
    }

    [Fact]
    public async Task A_403_snackbars_that_the_role_does_not_allow_it()
    {
        SignIn();
        inner.Status = HttpStatusCode.Forbidden;

        await SendAsync();

        Assert.Contains(Snackbar.ShownSnackbars, shown => shown.Message == Voice.RoleNotAllowed);
        Assert.True(session.IsSignedIn);
        Assert.Empty(Navigation.History);
    }

    [Fact]
    public async Task A_409_snackbars_that_the_record_already_changed()
    {
        SignIn();
        inner.Status = HttpStatusCode.Conflict;

        await SendAsync();

        Assert.Contains(Snackbar.ShownSnackbars, shown => shown.Message == Voice.AlreadyChanged);
        Assert.True(session.IsSignedIn);
        Assert.Empty(Navigation.History);
    }

    [Fact]
    public async Task The_progress_counter_rises_during_a_send_and_returns_to_zero()
    {
        await SendAsync();

        Assert.Equal(1, inner.InFlightDuringSend);
        Assert.Equal(0, loading.InFlight);
    }

    [Fact]
    public async Task The_progress_counter_returns_to_zero_on_a_failure_response()
    {
        inner.Status = HttpStatusCode.InternalServerError;

        await SendAsync();

        Assert.Equal(0, loading.InFlight);
    }

    [Fact]
    public async Task The_progress_counter_returns_to_zero_on_a_thrown_send()
    {
        // Without the finally the bar would stay on screen for the rest of the session.
        inner.Throws = new HttpRequestException("no route to host");

        await Assert.ThrowsAsync<HttpRequestException>(SendAsync);

        Assert.Equal(0, loading.InFlight);
    }

    private BunitNavigationManager Navigation =>
        (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();

    private ISnackbar Snackbar => Services.GetRequiredService<ISnackbar>();

    private void SignIn() =>
        session.SignIn(new SignedInUser("jwt", DateTimeOffset.UnixEpoch, Guid.Empty, "Marcus Bell", "Lead"));

    private async Task<HttpResponseMessage> SendAsync()
    {
        SessionMessageHandler handler = new(session, loading, Snackbar, Navigation) { InnerHandler = inner };
        using HttpClient client = new(handler) { BaseAddress = new Uri("http://localhost/") };

        return await client.GetAsync("api/v1/users", Xunit.TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The network's stand-in. It records the request it was given and the progress counter as it
    /// stood mid-send, which is how "the bar goes up" is observed rather than assumed.
    /// </summary>
    private sealed class StubInnerHandler : HttpMessageHandler
    {
        internal HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        internal Exception? Throws { get; set; }

        internal HttpRequestMessage? LastRequest { get; private set; }

        internal int InFlightDuringSend { get; private set; } = -1;

        internal LoadingState? Observing { get; set; }

        /// <summary>Awaited before answering, so a test can hold two sends open at once.</summary>
        internal Task? Gate { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            InFlightDuringSend = Observing?.InFlight ?? -1;

            if (Gate is not null)
            {
                await Gate;
            }

            return Throws is null ? new HttpResponseMessage(Status) : throw Throws;
        }
    }
}
