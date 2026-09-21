using ActionLedger.Web.Core.Api;
using ActionLedger.Web.Core.Auth;
using ActionLedger.Web.Core.Shell;
using ActionLedger.Web.Core.Theme;
using ActionLedger.Web.Core.Users;
using ActionLedger.Web.Layout;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Extensions;
using MudBlazor.Services;
using Xunit;

namespace ActionLedger.Web.Tests;

/// <summary>
/// DESIGN.md and AD-14 — the shell Story 1.6 fills in. MudBlazor's providers are mounted, the
/// theme the provider is given is the app's own rather than an implicit default, dark mode
/// follows the OS, and content sits in the 1280px container with 24px gutters that DESIGN.md's
/// <c>content-max</c> and <c>page-gutter</c> name.
/// </summary>
public sealed class LayoutTests : BunitContext
{
    private const string BodyProbeId = "body-probe";

    public LayoutTests()
    {
        Services.AddMudServices();

        // The shell injects both cross-feature state services and the roster. None of them is
        // what these five assertions are about — they are here so the layout can be rendered at
        // all — and every one of them is exercised on its own in ShellTests.
        Services.AddSingleton<IActionLedgerApiClient>(new StubApiClient());
        Services.AddSingleton(new SessionState());
        Services.AddSingleton(new LoadingState());
        Services.AddSingleton<UserDirectory>();

        // MudBlazor's providers call into JS as they mount; none of that is what is under test.
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void The_layout_mounts_every_mudblazor_provider()
    {
        IRenderedComponent<MainLayout> layout = RenderLayout();

        Assert.True(layout.HasComponent<MudThemeProvider>());
        Assert.True(layout.HasComponent<MudPopoverProvider>());
        Assert.True(layout.HasComponent<MudDialogProvider>());
        Assert.True(layout.HasComponent<MudSnackbarProvider>());
    }

    [Fact]
    public void The_theme_provider_is_given_the_apps_own_theme()
    {
        MudThemeProvider provider = RenderLayout().FindComponent<MudThemeProvider>().Instance;

        // Not merely "a theme": the one named seam a later story changes. An implicit default
        // here would leave nowhere to put a palette change when one is finally needed.
        Assert.Same(ActionLedgerTheme.Instance, provider.Theme);
    }

    [Fact]
    public void The_theme_provider_follows_the_operating_system_preference()
    {
        MudThemeProvider provider = RenderLayout().FindComponent<MudThemeProvider>().Instance;

        // The same signal tokens.css keys its dark scope off. If this were false the two layers
        // would disagree: MudBlazor light, the brand tokens dark.
        Assert.True(provider.GetState(theme => theme.ObserveSystemDarkModeChange));
    }

    [Fact]
    public void Content_sits_in_the_stock_large_container_with_gutters()
    {
        MudContainer container = RenderLayout().FindComponent<MudContainer>().Instance;

        // MaxWidth.Large is 1280px and gutters are 24px in the shipped MudBlazor stylesheet, so
        // DESIGN.md's content-max and page-gutter are reached without restyling anything.
        Assert.Equal(MaxWidth.Large, container.MaxWidth);
        Assert.True(container.Gutters);
    }

    [Fact]
    public void The_body_renders_inside_the_content_container()
    {
        IRenderedComponent<MainLayout> layout = RenderLayout();

        AngleSharp.Dom.IElement container = layout.Find(".mud-container");

        Assert.NotNull(container.QuerySelector($"#{BodyProbeId}"));
        Assert.Contains("mud-container-maxwidth-lg", container.ClassList, StringComparer.Ordinal);
        Assert.Contains("mud-container--gutters", container.ClassList, StringComparer.Ordinal);
    }

    private IRenderedComponent<MainLayout> RenderLayout() =>
        Render<MainLayout>(parameters => parameters.Add(
            layout => layout.Body,
            (RenderFragment)(builder => builder.AddMarkupContent(0, $"""<p id="{BodyProbeId}">body</p>"""))));
}
