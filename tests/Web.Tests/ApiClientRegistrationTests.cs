using System.Reflection;
using ActionLedger.Web.Core;
using ActionLedger.Web.Core.Api;
using ActionLedger.Web.Core.Auth;
using ActionLedger.Web.Core.Shell;
using ActionLedger.Web.Core.Users;
using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Xunit;

namespace ActionLedger.Web.Tests;

/// <summary>
/// AD-14 — the composition root's own wiring. Nothing else in the suite executes
/// <c>AddActionLedgerApiClient</c>: every other test constructs its subject directly, so reverting
/// this method to a bare <c>new HttpClient { BaseAddress = ... }</c> would leave the build
/// zero-warning and all of them green while no <c>Authorization</c> header was ever attached, the
/// 401/403/409 branches never ran, and the progress bar never appeared.
/// </summary>
public sealed class ApiClientRegistrationTests : BunitContext
{
    public ApiClientRegistrationTests()
    {
        // ISnackbar for the handler, and bUnit's NavigationManager, which the WebAssembly host
        // supplies in the real composition root.
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;

        Services.AddActionLedgerApiClient(new ConfigurationBuilder().Build(), HostOrigin);
    }

    [Fact]
    public void The_http_client_sends_through_the_session_handler()
    {
        HttpClient client = Services.GetRequiredService<HttpClient>();

        // The whole story hangs off this one link. Read by reflection because HttpClient exposes
        // no way to ask; if a runtime ever renames the field, this fails loudly rather than
        // quietly passing.
        Assert.IsType<SessionMessageHandler>(FieldOf(client, "_handler"));
    }

    [Fact]
    public void The_session_handler_is_chained_in_front_of_the_browsers_own_handler()
    {
        Services.GetRequiredService<HttpClient>();

        SessionMessageHandler handler = Services.GetRequiredService<SessionMessageHandler>();

        // In Blazor WebAssembly HttpClientHandler resolves to the browser's fetch handler, so
        // this is the supported shape rather than a workaround.
        Assert.IsType<HttpClientHandler>(handler.InnerHandler);
    }

    [Fact]
    public void The_http_client_does_not_claim_ownership_of_the_handler()
    {
        HttpClient client = Services.GetRequiredService<HttpClient>();

        // The container owns the scoped handler and disposes it. Left at the default the
        // HttpClient would claim it too, and whichever disposed first would leave the other
        // holding a disposed handler.
        Assert.Equal(false, FieldOf(client, "_disposeHandler"));
    }

    [Fact]
    public void The_base_address_falls_back_to_the_hosts_own_origin()
    {
        // The compose and single-origin case, where nginx serves the app and proxies /api. No
        // URL is committed anywhere (NFR5).
        Assert.Equal(new Uri(HostOrigin), Services.GetRequiredService<HttpClient>().BaseAddress);
    }

    [Fact]
    public void The_http_client_has_no_timeout_of_its_own()
    {
        // Spine :192 — no client-side timeout on the run POST, which may take up to the 180-second
        // run ceiling. Left at the .NET default of 100 seconds, a slow local-model run would be
        // cut off mid-run and reported as a failure while the server went on to persist it.
        Assert.Equal(Timeout.InfiniteTimeSpan, Services.GetRequiredService<HttpClient>().Timeout);
    }

    [Fact]
    public void A_configured_base_address_wins_over_the_host_origin()
    {
        BunitContext context = new();
        context.Services.AddMudServices();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [ApiClientRegistration.BaseAddressKey] = "http://configured.invalid/",
            })
            .Build();

        context.Services.AddActionLedgerApiClient(configuration, HostOrigin);

        Assert.Equal(
            new Uri("http://configured.invalid/"),
            context.Services.GetRequiredService<HttpClient>().BaseAddress);
    }

    [Theory]
    [InlineData(typeof(SessionState))]
    [InlineData(typeof(LoadingState))]
    [InlineData(typeof(SessionMessageHandler))]
    [InlineData(typeof(UserDirectory))]
    [InlineData(typeof(IActionLedgerApiClient))]
    public void Every_service_the_shell_and_the_pages_inject_resolves(Type service)
    {
        // MainLayout, LoginPage, and the handler all take these by injection. A missing
        // registration is a runtime failure on first render, which no compiler catches.
        Assert.NotNull(Services.GetRequiredService(service));
    }

    [Fact]
    public void The_two_cross_feature_state_services_are_one_instance_each()
    {
        // Scoped is effectively singleton in a WebAssembly host, and the shell only re-renders
        // because it subscribes to the same SessionState and LoadingState the handler writes to.
        Assert.Same(Services.GetRequiredService<SessionState>(), Services.GetRequiredService<SessionState>());
        Assert.Same(Services.GetRequiredService<LoadingState>(), Services.GetRequiredService<LoadingState>());
    }

    private const string HostOrigin = "http://localhost/";

    private static object? FieldOf(HttpClient client, string name)
    {
        FieldInfo? field = typeof(HttpMessageInvoker)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.True(field is not null, $"HttpMessageInvoker no longer has a '{name}' field; this test needs rewriting.");

        return field!.GetValue(client);
    }
}
