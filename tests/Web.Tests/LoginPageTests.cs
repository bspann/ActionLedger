using ActionLedger.Web.Core;
using ActionLedger.Web.Core.Api;
using ActionLedger.Web.Core.Auth;
using ActionLedger.Web.Core.Shell;
using ActionLedger.Web.Core.Users;
using ActionLedger.Web.Features.Auth;
using ActionLedger.Web.Features.Auth.Data;
using ActionLedger.Web.Shared;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Xunit;

namespace ActionLedger.Web.Tests;

/// <summary>
/// FR-23 and UX-DR18 — the app's first routable component. A refusal is inline and stays on the
/// page, anything else is the load-failure notice with Retry, the button cannot fire twice, and a
/// captured route beats the default landing.
/// </summary>
/// <remarks>
/// AD-14 shows up here as an absence: this test file names no generated type except through the
/// stub client, because the page itself cannot see one.
/// </remarks>
public sealed class LoginPageTests : BunitContext
{
    private readonly StubApiClient client = new();
    private readonly SessionState session = new();

    public LoginPageTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;

        Services.AddSingleton<IActionLedgerApiClient>(client);
        Services.AddSingleton(session);
        Services.AddSingleton(new LoadingState());
        Services.AddSingleton<UserDirectory>();
        Services.AddSingleton<AuthService>();
    }

    [Fact]
    public void The_form_labels_both_fields_visibly()
    {
        // NFR-6's accessibility floor: every field has a visible label, not a placeholder.
        IRenderedComponent<LoginPage> page = Render<LoginPage>();

        Assert.Equal(Voice.Username, page.Find("label[for='username']").TextContent);
        Assert.Equal(Voice.Password, page.Find("label[for='password']").TextContent);
        Assert.Equal(Voice.SignIn, page.Find("button[type='submit']").TextContent);
    }

    [Fact]
    public async Task A_successful_sign_in_stores_the_session_loads_the_roster_and_lands_on_meetings()
    {
        client.SignInResult.Token = "jwt";
        client.SignInResult.User.DisplayName = "Marcus Bell";
        client.SignInResult.User.Role = Role.Lead;

        IRenderedComponent<LoginPage> page = Render<LoginPage>();
        await SubmitAsync(page, "marcus", "pw");

        Assert.True(session.IsSignedIn);
        Assert.Equal("jwt", session.Token);
        Assert.Equal("Marcus Bell", session.DisplayName);
        Assert.Equal("Lead", session.Role);

        // AD-14 — the roster loads once after sign-in, and nothing renders it until Epic 3.
        Assert.Equal(1, client.ListUsersCalls);

        Assert.Equal("/meetings", Navigation.History.First().Uri);
    }

    [Fact]
    public async Task A_captured_route_wins_over_the_default_landing_and_is_then_forgotten()
    {
        session.CaptureAttemptedRoute("/actions/7");

        IRenderedComponent<LoginPage> page = Render<LoginPage>();
        await SubmitAsync(page, "marcus", "pw");

        Assert.Equal("/actions/7", Navigation.History.First().Uri);
        Assert.Null(session.TakeAttemptedRoute());
    }

    [Fact]
    public async Task A_refusal_shows_the_inline_message_and_goes_nowhere()
    {
        client.SignInThrows = StubApiClient.Problem(401, "Authentication is required.");

        IRenderedComponent<LoginPage> page = Render<LoginPage>();
        await SubmitAsync(page, "marcus", "wrong");

        // UX-DR18 — under the button, not a snackbar, and it never says which half was wrong.
        Assert.Equal(Voice.SignInFailed, page.Find("#sign-in-failed").TextContent);
        Assert.False(session.IsSignedIn);
        Assert.Empty(Navigation.History);

        // Values are retained, so a mistyped password does not cost the username too.
        Assert.Equal("marcus", page.Find("#username").GetAttribute("value"));
        Assert.Equal("wrong", page.Find("#password").GetAttribute("value"));
    }

    [Fact]
    public async Task A_validation_refusal_is_treated_as_a_refusal_rather_than_a_crash()
    {
        client.SignInThrows = StubApiClient.Invalid("The request was not valid.");

        IRenderedComponent<LoginPage> page = Render<LoginPage>();
        await SubmitAsync(page, "marcus", string.Empty);

        Assert.Equal(Voice.SignInFailed, page.Find("#sign-in-failed").TextContent);
        Assert.False(session.IsSignedIn);
    }

    [Fact]
    public async Task An_unexpected_failure_shows_the_load_failure_notice_with_retry()
    {
        // A 500 arrives as a bare ApiException with no type and no title at all.
        client.SignInThrows = StubApiClient.Bare(500);

        IRenderedComponent<LoginPage> page = Render<LoginPage>();
        await SubmitAsync(page, "marcus", "pw");

        Assert.Contains(
            Voice.LoadFailurePrefix + Voice.UnexpectedFailureTitle,
            page.Markup,
            StringComparison.Ordinal);
        Assert.DoesNotContain(Voice.SignInFailed, page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Retry_resubmits_the_retained_values()
    {
        client.SignInThrows = StubApiClient.Bare(500);

        IRenderedComponent<LoginPage> page = Render<LoginPage>();
        await SubmitAsync(page, "marcus", "pw");

        client.SignInThrows = null;
        await page.Find($"#{LoadFailure.RetryId}").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        Assert.Equal(2, client.SignInCalls);
        Assert.Equal("marcus", client.LastSignInCommand?.Username);
        Assert.True(session.IsSignedIn);
    }

    [Fact]
    public async Task The_button_is_disabled_while_a_response_is_outstanding()
    {
        TaskCompletionSource gate = new();
        client.SignInGate = gate.Task;

        IRenderedComponent<LoginPage> page = Render<LoginPage>();
        Task submitted = SubmitAsync(page, "marcus", "pw");

        Assert.True(page.Find("button[type='submit']").HasAttribute("disabled"));

        // A second press cannot issue a second request while the first is outstanding.
        await page.Find("form").SubmitAsync();
        Assert.Equal(1, client.SignInCalls);

        gate.SetResult();
        await submitted;

        Assert.True(session.IsSignedIn);
    }

    [Fact]
    public void A_signed_in_visitor_is_sent_to_meetings_instead_of_the_form()
    {
        // EXPERIENCE.md calls this "a route-level auth check, not a router guard", so the page
        // checks for itself on initialise.
        session.SignIn(new SignedInUser("jwt", DateTimeOffset.UnixEpoch, Guid.Empty, "Marcus Bell", RoleNames.Lead));

        IRenderedComponent<LoginPage> page = Render<LoginPage>();

        Assert.Equal("/meetings", Navigation.History.First().Uri);

        // A navigation is not an unmount. Leaving the markup up would flash the form on the way
        // to Meetings and hand a screen reader a second username field to read.
        Assert.Empty(page.FindAll("form"));
        Assert.Empty(page.FindAll("#username"));
    }

    [Fact]
    public async Task A_new_sign_in_fetches_its_own_roster_rather_than_inheriting_the_last_one()
    {
        // A 401 expiry clears the session but not the roster — only the menu's sign-out does
        // that — and EnsureLoadedAsync returns early once IsLoaded is set. Without a clear here,
        // the next person to sign in on this tab sees the previous session's people.
        UserDirectory directory = Services.GetRequiredService<UserDirectory>();
        client.Roster.Items.Add(new UserSummaryDto { Id = Guid.Empty, DisplayName = "Stale", Role = Role.Lead });
        await directory.EnsureLoadedAsync(Xunit.TestContext.Current.CancellationToken);
        Assert.Equal("Stale", directory.Users.Single().DisplayName);

        client.Roster = new PagedResultOfUserSummaryDto
        {
            Items = [new UserSummaryDto { Id = Guid.Empty, DisplayName = "Marcus Bell", Role = Role.Lead }],
            Page = 1,
            PageSize = 200,
            Total = 1,
        };

        IRenderedComponent<LoginPage> page = Render<LoginPage>();
        await SubmitAsync(page, "marcus", "pw");

        Assert.Equal(2, client.ListUsersCalls);
        Assert.Equal("Marcus Bell", directory.Users.Single().DisplayName);
    }

    [Fact]
    public async Task The_inline_refusal_is_announced_and_named_by_the_password_field()
    {
        // The message appears with no focus change, so without role="alert" a screen-reader user
        // presses Sign in and hears nothing at all.
        client.SignInThrows = StubApiClient.Problem(401, "Authentication is required.");

        IRenderedComponent<LoginPage> page = Render<LoginPage>();

        Assert.Empty(page.FindAll("[role='alert']"));
        Assert.Null(page.Find("#password").GetAttribute("aria-describedby"));

        await SubmitAsync(page, "marcus", "wrong");

        Assert.Equal(Voice.SignInFailed, page.Find("[role='alert']").TextContent);
        Assert.Equal("sign-in-failed", page.Find("#password").GetAttribute("aria-describedby"));
    }

    [Fact]
    public void The_fields_carry_the_autocomplete_hints_a_password_manager_looks_for()
    {
        IRenderedComponent<LoginPage> page = Render<LoginPage>();

        Assert.Equal("username", page.Find("#username").GetAttribute("autocomplete"));
        Assert.Equal("current-password", page.Find("#password").GetAttribute("autocomplete"));
    }

    private BunitNavigationManager Navigation =>
        (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();

    private static Task SubmitAsync(IRenderedComponent<LoginPage> page, string username, string password)
    {
        page.Find("#username").Input(username);
        page.Find("#password").Input(password);

        return page.Find("form").SubmitAsync();
    }
}
