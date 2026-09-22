using ActionLedger.Web.Core;
using ActionLedger.Web.Core.Api;
using ActionLedger.Web.Core.Auth;
using ActionLedger.Web.Core.Shell;
using ActionLedger.Web.Core.Users;
using ActionLedger.Web.Features.Auth.Data;
using ActionLedger.Web.Features.Meetings.Data;
using ActionLedger.Web.Features.Review.Data;
using ActionLedger.Web.Layout;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Xunit;

namespace ActionLedger.Web.Tests;

/// <summary>
/// EXPERIENCE.md's Shell and UX-DR18 — one app bar for a signed-in session and none before it,
/// exactly two destinations with the current one marked, a user menu carrying display name, Role,
/// and Sign out, and one progress bar that follows <see cref="LoadingState"/>. There is no side
/// navigation.
/// </summary>
/// <remarks>
/// This extends <c>LayoutTests</c>' shape rather than replacing it: the four providers, the theme
/// binding, and the 1280px gutter container stay asserted there, and everything the shell adds is
/// asserted here.
/// </remarks>
public sealed class ShellTests : BunitContext
{
    private const string BodyProbeId = "body-probe";

    private readonly StubApiClient client = new();
    private readonly SessionState session = new();
    private readonly LoadingState loading = new();

    public ShellTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;

        Services.AddSingleton<IActionLedgerApiClient>(client);
        Services.AddSingleton(session);
        Services.AddSingleton(loading);
        Services.AddSingleton<UserDirectory>();

        // The router reaches LoginPage, the two Meetings pages, and Run Detail on a matched route,
        // and each injects its own feature's data service.
        Services.AddSingleton<AuthService>();
        Services.AddSingleton<MeetingsService>();
        Services.AddSingleton<ReviewService>();
    }

    [Fact]
    public void Before_sign_in_there_is_no_app_bar_and_no_nav()
    {
        IRenderedComponent<MainLayout> layout = RenderLayout();

        Assert.Empty(layout.FindAll("header"));
        Assert.Empty(layout.FindAll("a.mud-nav-link"));
        Assert.Empty(layout.FindAll("button.mud-menu-button-activator"));
    }

    [Fact]
    public void After_sign_in_the_app_bar_carries_the_product_name_and_both_destinations()
    {
        SignIn();

        IRenderedComponent<MainLayout> layout = RenderLayout();

        AngleSharp.Dom.IElement bar = layout.Find("header");

        AngleSharp.Dom.IElement home = bar.QuerySelector("a.mud-link")!;
        Assert.Equal(Voice.ProductName, home.TextContent);
        Assert.Equal("/meetings", home.GetAttribute("href"));

        AngleSharp.Dom.IElement[] destinations = [.. bar.QuerySelectorAll("a.mud-nav-link")];
        Assert.Equal(2, destinations.Length);
        Assert.Equal(Voice.Meetings, destinations[0].TextContent);
        Assert.Equal("/meetings", destinations[0].GetAttribute("href"));
        Assert.Equal(Voice.Actions, destinations[1].TextContent);
        Assert.Equal("/actions", destinations[1].GetAttribute("href"));
    }

    [Fact]
    public void There_is_no_side_navigation()
    {
        SignIn();

        IRenderedComponent<MainLayout> layout = RenderLayout();

        // EXPERIENCE.md: "A single top app bar ... No side navigation."
        Assert.Empty(layout.FindAll("aside"));
        Assert.Empty(layout.FindAll(".mud-drawer"));
    }

    [Theory]
    [InlineData("/meetings", 0)]
    [InlineData("/meetings/7", 0)]
    [InlineData("/actions", 1)]
    [InlineData("/actions/7", 1)]
    public void The_current_destination_carries_the_active_marker(string route, int expected)
    {
        SignIn();
        Navigation.NavigateTo(route);

        IRenderedComponent<MainLayout> layout = RenderLayout();

        AngleSharp.Dom.IElement[] destinations = [.. layout.FindAll("a.mud-nav-link")];

        // A detail route keeps its parent destination marked, which is why Match is Prefix.
        Assert.Equal("page", destinations[expected].GetAttribute("aria-current"));
        Assert.Null(destinations[1 - expected].GetAttribute("aria-current"));
    }

    [Fact]
    public void The_user_menu_shows_the_display_name_and_the_role()
    {
        SignIn();

        IRenderedComponent<MainLayout> layout = RenderLayout();

        AngleSharp.Dom.IElement trigger = layout.Find("button.mud-menu-button-activator");
        Assert.Equal("Marcus Bell", trigger.TextContent);

        // NFR-6 — the trigger has an accessible name rather than relying on the icon alone.
        Assert.Equal("Marcus Bell", trigger.GetAttribute("aria-label"));

        Assert.Contains("Lead", layout.Find("header").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void An_action_officers_role_reads_as_two_words_in_the_toolbar()
    {
        // Two of the three seeded demo users are Action Officers, so the wire spelling would be
        // on screen for most of the demo. Every other test here signs in a Lead, where the two
        // spellings happen to agree, which is exactly what hid this.
        session.SignIn(new SignedInUser(
            "jwt", DateTimeOffset.UnixEpoch, Guid.Empty, "Dana Whitfield", RoleNames.ActionOfficer));

        IRenderedComponent<MainLayout> layout = RenderLayout();

        Assert.Contains("Action Officer", layout.Find("header").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_user_menu_holds_exactly_one_item_and_signing_out_clears_the_session()
    {
        SignIn();

        // Loaded for real, so "the roster is cleared" is observable rather than vacuously true.
        client.Roster.Items.Add(new UserSummaryDto { Id = Guid.Empty, DisplayName = "Dana", Role = Role.ActionOfficer });
        UserDirectory directory = Services.GetRequiredService<UserDirectory>();
        await directory.EnsureLoadedAsync(Xunit.TestContext.Current.CancellationToken);
        Assert.NotEmpty(directory.Users);

        IRenderedComponent<MainLayout> layout = RenderLayout();
        layout.Find("button.mud-menu-button-activator").Click();

        AngleSharp.Dom.IElement item = layout.Find("[role='menuitem']");
        Assert.Equal(Voice.SignOut, item.TextContent);
        Assert.Single(layout.FindAll("[role='menuitem']"));

        item.Click();

        Assert.False(session.IsSignedIn);
        Assert.Empty(directory.Users);
        Assert.Equal("/login", Navigation.History.First().Uri);
    }

    [Fact]
    public void The_app_bar_disappears_again_after_signing_out()
    {
        SignIn();

        IRenderedComponent<MainLayout> layout = RenderLayout();
        Assert.Single(layout.FindAll("header"));

        // The shell re-renders off SessionState.Changed and nothing else.
        session.SignOut();

        Assert.Empty(layout.FindAll("header"));
    }

    [Theory]
    [InlineData("/nope")]
    [InlineData("/actions")]
    public void An_unmatched_route_renders_the_not_found_notice_inside_the_shell(string route)
    {
        // /actions is unmatched until Story 4.3 lands, which is the state the epic plans for: an
        // honest Not found inside a working shell rather than a placeholder page that exists only
        // to be deleted next story. /meetings left this list when Story 2.2 gave it a real page.
        SignIn();
        Navigation.NavigateTo(route);

        IRenderedComponent<App> app = Render<App>();

        Assert.Equal(Voice.NotFound, app.Find("h1").TextContent);

        // Inside the shell, not instead of it: the toolbar and the gutter container are both here.
        Assert.Single(app.FindAll("header"));
        AngleSharp.Dom.IElement container = app.Find(".mud-container");
        Assert.Equal(Voice.NotFound, container.QuerySelector("h1")!.TextContent);
        Assert.Equal("/meetings", container.QuerySelector("a")!.GetAttribute("href"));
    }

    [Fact]
    public void The_login_route_is_matched_rather_than_not_found()
    {
        // The non-vacuity half: if the router matched nothing at all, the theory above would pass
        // for the wrong reason.
        Navigation.NavigateTo("/login");

        IRenderedComponent<App> app = Render<App>();

        Assert.Equal(Voice.ProductName, app.Find("h1").TextContent);
    }

    [Fact]
    public void The_meetings_route_is_matched_rather_than_not_found()
    {
        // Story 2.2's half of the same check: /meetings is a real page now, reached through the
        // router and rendered inside the shell rather than falling through to the notice.
        SignIn();
        Navigation.NavigateTo("/meetings");

        IRenderedComponent<App> app = Render<App>();

        Assert.Equal(Voice.Meetings, app.Find(".mud-container h1").TextContent);
    }

    [Fact]
    public void The_meeting_detail_route_template_is_matched_and_binds_its_id()
    {
        // MeetingDetailPageTests hands Id in as a parameter and never goes through the router, so
        // nothing else parses "/meetings/{Id:guid}". Rename or mistype that template and every
        // navigation from the list lands on Not found with the whole suite still green.
        SignIn();
        client.Meeting.Title = "Office move follow-up";
        Navigation.NavigateTo("/meetings/01999999-0000-7000-8000-00000000beef");

        IRenderedComponent<App> app = Render<App>();

        app.WaitForAssertion(() =>
            Assert.Equal("Office move follow-up", app.Find(".mud-container h1").TextContent));
        Assert.Equal(
            Guid.Parse("01999999-0000-7000-8000-00000000beef"),
            client.LastGetMeetingId);
    }

    [Fact]
    public void The_run_detail_route_template_is_matched_and_binds_both_ids()
    {
        // RunDetailPageTests hands both ids in as parameters, so nothing else parses
        // "/meetings/{MeetingId:guid}/runs/{RunId:guid}". A mistyped template sends every run row
        // to Not found with the rest of the suite green — and a swapped pair would read the
        // meeting id as a run id, which the meeting check below is what catches.
        Guid meetingId = Guid.Parse("01999999-0000-7000-8000-00000000beef");
        Guid runId = Guid.Parse("01999999-0000-7000-8000-0000000000aa");

        SignIn();
        client.Run.Id = runId;
        client.Run.MeetingId = meetingId;
        Navigation.NavigateTo($"/meetings/{meetingId}/runs/{runId}");

        IRenderedComponent<App> app = Render<App>();

        app.WaitForAssertion(() => Assert.Equal("Fake", app.Find("#" + Features.Review.RunDetailPage.ProviderId).TextContent));
        Assert.Equal(Voice.ExtractionRun, app.Find(".mud-container h1").TextContent);
        Assert.Equal(runId, client.LastGetExtractionRunId);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/login")]
    public void A_signed_out_visitor_reaches_the_login_form_on_either_route(string route)
    {
        // LoginPage carries both @page directives: "/" is where an unauthenticated visitor lands
        // and "/login" is where the delegating handler sends an expired session. Asserting the
        // form itself rather than only the heading is what separates "the login form rendered"
        // from "some page with the product name in its h1 rendered".
        Navigation.NavigateTo(route);

        IRenderedComponent<App> app = Render<App>();

        Assert.Equal(Voice.ProductName, app.Find("h1").TextContent);
        Assert.Single(app.FindAll("form"));
        Assert.Equal(Voice.SignIn, app.Find("form button[type='submit']").TextContent.Trim());

        // Signed out, so the shell offers nothing to navigate as.
        Assert.Empty(app.FindAll("header"));
    }

    [Fact]
    public void There_is_no_progress_bar_while_nothing_is_in_flight()
    {
        IRenderedComponent<MainLayout> layout = RenderLayout();

        Assert.Empty(layout.FindAll(".al-progress"));
    }

    [Fact]
    public void The_progress_bar_follows_the_loading_state()
    {
        IRenderedComponent<MainLayout> layout = RenderLayout();

        loading.Begin();
        Assert.Single(layout.FindAll(".al-progress"));

        // Two overlapping requests: the first to finish must not switch the bar off under the
        // second, which is why LoadingState counts rather than flags.
        loading.Begin();
        loading.End();
        Assert.Single(layout.FindAll(".al-progress"));

        loading.End();
        Assert.Empty(layout.FindAll(".al-progress"));
    }

    [Fact]
    public void The_progress_bar_sits_under_the_app_bar_only_when_there_is_one()
    {
        IRenderedComponent<MainLayout> layout = RenderLayout();

        loading.Begin();
        Assert.DoesNotContain(
            "al-progress--under-app-bar",
            layout.Find(".al-progress").ClassName!,
            StringComparison.Ordinal);

        SignIn();

        Assert.Contains(
            "al-progress--under-app-bar",
            layout.Find(".al-progress").ClassName!,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_offset_class_the_shell_names_is_the_one_the_stylesheet_defines()
    {
        // The class names are the contract between MainLayout and app.css, and nothing else
        // checks that both halves agree. tokens.css is untouched: it is DESIGN.md's file.
        string css = WebProject.ReadAllText("wwwroot/css/app.css");

        Assert.Contains(".al-progress {", css, StringComparison.Ordinal);
        Assert.Contains(".al-progress--under-app-bar {", css, StringComparison.Ordinal);

        // Meeting Detail's read-only notes block names the same kind of contract, and it carries
        // the visible half of ADR-002: white-space: pre-wrap is what makes the stored text render
        // as it was hashed. bUnit applies no stylesheet, so the page tests can only see the class
        // attribute — delete the rule and every one of them stays green.
        Assert.Contains(".al-notes {", css, StringComparison.Ordinal);
        Assert.Contains(
            "<link rel=\"stylesheet\" href=\"css/app.css\" />",
            WebProject.ReadAllText("wwwroot/index.html"),
            StringComparison.Ordinal);
    }

    private BunitNavigationManager Navigation =>
        (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();

    private void SignIn() =>
        session.SignIn(new SignedInUser(
            "jwt", DateTimeOffset.UnixEpoch, Guid.Empty, "Marcus Bell", RoleNames.Lead));

    private IRenderedComponent<MainLayout> RenderLayout() =>
        Render<MainLayout>(parameters => parameters.Add(
            layout => layout.Body,
            (RenderFragment)(builder => builder.AddMarkupContent(0, $"""<p id="{BodyProbeId}">body</p>"""))));
}
