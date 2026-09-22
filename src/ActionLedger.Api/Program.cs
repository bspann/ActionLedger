using System.Text.Json.Serialization;
using ActionLedger.Api.Auth;
using ActionLedger.Api.Configuration;
using ActionLedger.Api.Errors;
using ActionLedger.Api.Health;
using ActionLedger.Api.Observability;
using ActionLedger.Api.OpenApi;
using ActionLedger.Api.Routing;
using ActionLedger.Application;
using ActionLedger.Application.Abstractions;
using ActionLedger.Infrastructure;
using ActionLedger.Infrastructure.Ai;
using ActionLedger.Infrastructure.Ai.Providers;
using ActionLedger.Infrastructure.Persistence;
using ActionLedger.Infrastructure.Seed;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Formatting.Compact;

// The composition root. Story 1.2 establishes the contract every later story lands on:
// /api/v1 routing, RFC 9457 ProblemDetails, validated options, bearer authentication,
// structured logs with a correlation id, and the OpenAPI document the web client is built from.

(string? outputPath, bool exportContract, string[] hostArgs) = OpenApiExport.ParseCommandLine(args);

WebApplicationBuilder builder = WebApplication.CreateBuilder(hostArgs);

// Structured JSON on stdout, one object per line, correlationId on every one of them.
builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.With(new CorrelationIdEnricher())
    .WriteTo.Console(new CompactJsonFormatter()));

builder.Services.AddApiOptions(builder.Configuration);
builder.Services.AddApiAuthentication();
builder.Services.AddApiProblemDetails();

// AD-10 — every EF Core type stays inside Infrastructure; the composition root only hands it the
// validated configuration. AD-17 — nothing here migrates: the schema arrives through the bundle.
builder.Services.AddActionLedgerPersistence(services =>
    services.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString);

// AD-11, AD-16 — the AI ring takes validated values, never configuration, exactly as the seeder
// does. Registered before AddActionLedgerSeeding because hosted services start in registration
// order: a prompt version with no file, a provider with no factory, or a provider whose probe fails
// (an unreachable local server, a missing Azure credential) stops the host here, before any demo
// row is written.
// FR-7 — each provider's sub-section travels in its own settings record, so the Azure api key never
// rides in AiSettings. Only the active provider's section is validated; the others arrive empty.
builder.Services.AddActionLedgerAi(
    services =>
    {
        AiOptions ai = services.GetRequiredService<IOptions<AiOptions>>().Value;

        return new AiSettings(ai.Provider, ai.PromptVersion, ai.CallTimeoutSeconds, ai.LowConfidenceThreshold);
    },
    services =>
    {
        AiOptions.LocalOpenAIOptions local = services.GetRequiredService<IOptions<AiOptions>>().Value.LocalOpenAI;

        return new LocalOpenAISettings(local.BaseUrl, local.Model);
    },
    services =>
    {
        AiOptions.AzureOpenAIOptions azure = services.GetRequiredService<IOptions<AiOptions>>().Value.AzureOpenAI;

        return new AzureOpenAISettings(azure.Endpoint, azure.Model, azure.ApiKey);
    });

// AD-21 — the seeder is a hosted service in the api, gated on Seed:Enabled. Registered here,
// before the outbox dispatcher the webhook epic adds, so it completes before the first poll.
builder.Services.AddActionLedgerSeeding(services =>
{
    SeedOptions seed = services.GetRequiredService<IOptions<SeedOptions>>().Value;

    return new SeedSettings(seed.Enabled, seed.DefaultPassword);
});

// AD-2 — the handlers and per-feature query classes. The ring registers its own use cases; the
// composition root only adds the ports it is the right ring to own.
builder.Services.AddActionLedgerApplication();

// AD-12 — the two Api-ring ports. They live here, not in Infrastructure: the token issuer needs
// JwtOptions, the current user needs HttpContext, and AD-1 Rule 4 forbids Infrastructure seeing
// either. ICurrentUser throws when nothing authenticated the request, so it is only ever resolved
// behind [Authorize].
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
builder.Services.AddScoped<ICurrentUser, ClaimsPrincipalCurrentUser>();

builder.Services
    .AddControllers(options => options.Conventions.Add(new ApiRoutePrefixConvention()))
    .AddJsonOptions(options =>
        // Enums cross the boundary as PascalCase strings (Consistency Conventions, Enums row).
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddApiOpenApi();

WebApplication app = builder.Build();

app.UseRequestCorrelation();
app.UseApiProblemDetails();
app.UseSerilogRequestLogging();

app.UseAuthentication();
app.UseAuthorization();

// Liveness. Answers without touching a database, so a probe against it never reports the api
// down because PostgreSQL is slow (AD-17).
app.MapGet("/health", () => TypedResults.Ok(HealthStatus.Healthy))
    .WithName("GetHealth")
    .WithTags("Health")
    .WithSummary("Liveness. Returns 200 whenever the process is serving; it touches no dependency.");

// Readiness. 200 only when the database actually answers; 503 otherwise, never 200 and never an
// unhandled exception (AD-17). This is the Container Apps readiness probe and what `migrate`
// having finished successfully looks like from outside.
app.MapGet(
        "/health/ready",
        async Task<Results<Ok<HealthStatus>, JsonHttpResult<HealthStatus>>> (
            DatabaseReadiness readiness,
            CancellationToken cancellationToken) =>
            await readiness.IsReadyAsync(cancellationToken)
                ? TypedResults.Ok(HealthStatus.Ready)
                : TypedResults.Json(HealthStatus.Unavailable, statusCode: StatusCodes.Status503ServiceUnavailable))
    .WithName("GetReadiness")
    .WithTags("Health")
    .WithSummary("Readiness. Returns 200 only when the database is reachable, and 503 when it is not.")
    // JsonHttpResult carries no status code in its metadata, so the 503 has to be declared for
    // the contract — and therefore for the generated client — to know about it.
    .Produces<HealthStatus>(StatusCodes.Status503ServiceUnavailable);

app.MapOpenApi();

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment(ApiEnvironments.Compose))
{
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint($"/openapi/{OpenApiSetup.DocumentName}.json", "ActionLedger v1");
        options.RoutePrefix = "swagger";
        options.DocumentTitle = "ActionLedger API";
    });
}

app.MapControllers();

if (exportContract)
{
    return await OpenApiExport.WriteAsync(app, outputPath);
}

app.Run();

return 0;

/// <summary>Exposed so <c>Api.Tests</c> can drive this host through <c>WebApplicationFactory</c>.</summary>
public partial class Program;
