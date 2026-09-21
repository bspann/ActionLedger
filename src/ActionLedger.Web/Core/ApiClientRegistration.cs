using ActionLedger.Web.Core.Api;
using ActionLedger.Web.Core.Auth;
using ActionLedger.Web.Core.Shell;
using ActionLedger.Web.Core.Users;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ActionLedger.Web.Core;

/// <summary>
/// AD-14 — the one place an <see cref="HttpClient"/> is constructed and handed to the generated
/// client. Everything above this line talks to <c>IActionLedgerApiClient</c>; nothing above it
/// sees <c>System.Net.Http</c>.
/// </summary>
/// <remarks>
/// It sits in <c>Core</c> rather than <c>Core/Api</c> on purpose. <c>Core/Api</c> is the
/// git-ignored folder the generator writes into, and the composition root has to call this
/// method — so a registration inside <c>Core.Api</c> would both be untracked on a clean clone and
/// drag <c>Program</c> across the AD-14 seam. Here the composition root depends on
/// <c>ActionLedger.Web.Core</c> and never on the generated namespace.
/// </remarks>
public static class ApiClientRegistration
{
    /// <summary>
    /// Configuration key for the API origin. Absent — which is the compose and single-origin
    /// case, where nginx serves the app and proxies <c>/api</c> — the host's own origin is used.
    /// No URL is committed anywhere (NFR5).
    /// </summary>
    public const string BaseAddressKey = "Api:BaseAddress";

    public static IServiceCollection AddActionLedgerApiClient(
        this IServiceCollection services,
        IConfiguration configuration,
        string hostBaseAddress)
    {
        string baseAddress = configuration[BaseAddressKey] is { Length: > 0 } configured
            ? configured
            : hostBaseAddress;

        // AD-14's two cross-feature state services, plus the shell's one loading signal. Scoped is
        // effectively singleton in a WebAssembly host, so the handler, the layout, and every page
        // share one instance of each.
        services.AddScoped<SessionState>();
        services.AddScoped<LoadingState>();
        services.AddScoped<SessionMessageHandler>();

        // Chained by hand rather than with AddHttpClient: Microsoft.Extensions.Http is not a
        // pinned package and NFR9 keeps the list short. In Blazor WebAssembly HttpClientHandler
        // resolves to the browser's fetch handler, so this is the supported shape.
        services.AddScoped(provider =>
        {
            SessionMessageHandler handler = provider.GetRequiredService<SessionMessageHandler>();
            handler.InnerHandler = new HttpClientHandler();

            // disposeHandler: false — the container owns the scoped SessionMessageHandler and
            // disposes it itself. Left at the default, the HttpClient would claim it too, and
            // whichever disposed first would leave the other holding a disposed handler.
            return new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri(baseAddress) };
        });

        services.AddScoped<IActionLedgerApiClient>(
            provider => new ActionLedgerApiClient(provider.GetRequiredService<HttpClient>()));

        services.AddScoped<UserDirectory>();

        return services;
    }
}
