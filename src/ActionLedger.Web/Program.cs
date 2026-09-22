using ActionLedger.Web;
using ActionLedger.Web.Core;
using ActionLedger.Web.Features.Auth.Data;
using ActionLedger.Web.Features.Meetings.Data;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

// The composition root. AddMudServices registers ISnackbar and IDialogService, which the
// delegating handler and the shell both depend on, so it has to run before the client is wired.

WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddMudServices();

// AD-14 — the HttpClient, the delegating handler, the generated client, and the two cross-feature
// state services are all constructed in Core and nowhere else.
builder.Services.AddActionLedgerApiClient(builder.Configuration, builder.HostEnvironment.BaseAddress);

// Per-feature data services. Each one wraps the generated client; nothing above them sees HTTP.
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<MeetingsService>();

await builder.Build().RunAsync();
