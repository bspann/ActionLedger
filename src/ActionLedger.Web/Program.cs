using ActionLedger.Web;
using ActionLedger.Web.Core;
using ActionLedger.Web.Features.Auth.Data;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

// The composition root. Story 1.5 ships the scaffold Story 1.6 fills in: the router, the MudBlazor
// providers, the theme, the design tokens, and the generated client behind the AD-14 seam. There is
// deliberately no routable component yet — every page, the session, and the shell are Story 1.6.

WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddMudServices();

// AD-14 — the HttpClient and the generated client are constructed in Core and nowhere else.
builder.Services.AddActionLedgerApiClient(builder.Configuration, builder.HostEnvironment.BaseAddress);

// Per-feature data services. Each one wraps the generated client; nothing above them sees HTTP.
builder.Services.AddScoped<AuthService>();

await builder.Build().RunAsync();
