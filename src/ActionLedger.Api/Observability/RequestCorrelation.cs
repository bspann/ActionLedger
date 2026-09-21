using System.Diagnostics;
using Serilog.Context;
using Serilog.Core;
using Serilog.Events;

namespace ActionLedger.Api.Observability;

/// <summary>
/// The correlation id every log line carries (Consistency Conventions, Logging and correlation
/// row): the W3C trace id when a trace context is in flight, otherwise the request's trace
/// identifier.
/// </summary>
/// <remarks>
/// The trace id is preferred because it is the same value on the caller's side, so one id follows
/// a request across the web app, the api, and — from Epic 5 — the outbox dispatcher and whatever
/// an integrator runs behind a webhook.
/// </remarks>
public static class RequestCorrelation
{
    /// <summary>The property name on every log line and on every ProblemDetails body.</summary>
    public const string PropertyName = "correlationId";

    /// <summary>Where the resolved id is cached for the life of the request.</summary>
    private const string ItemKey = "ActionLedger.CorrelationId";

    /// <summary>The value used on log lines written outside any request — startup, shutdown, hosted services.</summary>
    internal const string OutsideRequest = "host";

    /// <summary>Resolves — and then caches — the correlation id for this request.</summary>
    public static string For(HttpContext context)
    {
        if (context.Items.TryGetValue(ItemKey, out object? cached) && cached is string resolved)
        {
            return resolved;
        }

        string correlationId = TraceId() ?? context.TraceIdentifier;
        context.Items[ItemKey] = correlationId;

        return correlationId;
    }

    /// <summary>Pushes the correlation id onto Serilog's log context for the rest of the request.</summary>
    public static IApplicationBuilder UseRequestCorrelation(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            using (LogContext.PushProperty(PropertyName, For(context)))
            {
                await next(context);
            }
        });

    private static string? TraceId()
    {
        Activity? activity = Activity.Current;

        return activity is { IdFormat: ActivityIdFormat.W3C } && activity.TraceId != default
            ? activity.TraceId.ToString()
            : null;
    }
}

/// <summary>
/// Guarantees the <c>correlationId</c> property on lines written outside a request, so "every log
/// line carries correlationId" holds for startup and hosted-service output too. Inside a request
/// the log context has already supplied it and this enricher leaves it alone.
/// </summary>
internal sealed class CorrelationIdEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        string correlationId = Activity.Current is { IdFormat: ActivityIdFormat.W3C } activity && activity.TraceId != default
            ? activity.TraceId.ToString()
            : RequestCorrelation.OutsideRequest;

        logEvent.AddPropertyIfAbsent(
            propertyFactory.CreateProperty(RequestCorrelation.PropertyName, correlationId));
    }
}
