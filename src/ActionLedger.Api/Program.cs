using System.Text.Json.Serialization;
using ActionLedger.Api.Auth;
using ActionLedger.Api.Configuration;
using ActionLedger.Api.Errors;
using ActionLedger.Api.Health;
using ActionLedger.Api.Observability;
using ActionLedger.Api.OpenApi;
using ActionLedger.Api.Routing;
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

// Liveness. Answers without touching a database — /health/ready is Story 1.3's, when there is
// a database to be ready for.
app.MapGet("/health", () => TypedResults.Ok(HealthStatus.Healthy))
    .WithName("GetHealth")
    .WithTags("Health")
    .WithSummary("Liveness. Returns 200 whenever the process is serving; it touches no dependency.");

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
