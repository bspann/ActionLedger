using ActionLedger.Web.Core.Api;
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

        services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(baseAddress) });
        services.AddScoped<IActionLedgerApiClient>(
            provider => new ActionLedgerApiClient(provider.GetRequiredService<HttpClient>()));

        return services;
    }
}
