using ActionLedger.Api.Observability;
using ActionLedger.Application.Abstractions;
using ActionLedger.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ActionLedger.Api.Errors;

/// <summary>
/// AD-13 — every failure leaves the Api as an RFC 9457 ProblemDetails with a <c>type</c> from a
/// fixed set of five. This file is the only place that decides what an error looks like on the
/// wire: the exception mapping, the bare-status-code mapping, and the model-validation mapping
/// all funnel through <see cref="ProblemTypes"/> and <see cref="Decorate"/>.
/// </summary>
public static class ProblemDetailsMapping
{
    /// <summary>
    /// Registers the ProblemDetails writer, the exception-to-status mapping, and the model
    /// validation response. Pair with <see cref="UseApiProblemDetails"/>.
    /// </summary>
    public static IServiceCollection AddApiProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
            Decorate(context.HttpContext, context.ProblemDetails));

        services.AddExceptionHandler<ApiExceptionHandler>();

        // MVC builds its own 400 for a failed [ApiController] model binding and does not route it
        // through CustomizeProblemDetails. Map it here so a validation failure carries the same
        // type, correlation id, and content type as every other error.
        services.Configure<ApiBehaviorOptions>(options =>
            options.InvalidModelStateResponseFactory = context =>
            {
                ValidationProblemDetails problem = new(context.ModelState)
                {
                    Status = StatusCodes.Status400BadRequest,
                };

                Decorate(context.HttpContext, problem);

                return new BadRequestObjectResult(problem)
                {
                    ContentTypes = { "application/problem+json" },
                };
            });

        return services;
    }

    /// <summary>
    /// Puts the exception handler and the bare-status-code handler at the front of the pipeline.
    /// The status-code handler is what turns an unmapped path, a 401 challenge, and a 403 into
    /// ProblemDetails instead of an empty body or an HTML error page.
    /// </summary>
    public static WebApplication UseApiProblemDetails(this WebApplication app)
    {
        app.UseExceptionHandler();

        app.UseStatusCodePages(async context =>
        {
            HttpContext http = context.HttpContext;

            if (http.Response.HasStarted)
            {
                return;
            }

            IProblemDetailsService problems = http.RequestServices.GetRequiredService<IProblemDetailsService>();

            await problems.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = http,
                ProblemDetails = { Status = http.Response.StatusCode },
            });
        });

        return app;
    }

    /// <summary>
    /// Applies the AD-13 <c>type</c> slug for the status, an <c>instance</c> pointing at the
    /// request, and the correlation id that ties the response to the log line that recorded it.
    /// </summary>
    internal static void Decorate(HttpContext http, ProblemDetails problem)
    {
        int status = problem.Status ??= http.Response.StatusCode;

        if (ProblemTypes.For(status) is { } slug)
        {
            problem.Type = slug;

            // One title per status, not the framework's reason phrase. The web app renders it
            // verbatim — "Couldn't load. {problem title}" — so it has to read as a sentence, and
            // it has to be the same sentence every time. Anything specific to the failure goes in
            // `detail`, which the caller may set freely.
            problem.Title = ProblemTypes.TitleFor(status);
        }

        problem.Instance ??= http.Request.GetEncodedPathAndQuery();

        // `traceId` is the framework's default extension and carries the same trace in a second
        // format. One correlation field, under the name the Logging convention uses.
        problem.Extensions.Remove("traceId");
        problem.Extensions[RequestCorrelation.PropertyName] = RequestCorrelation.For(http);
    }
}

/// <summary>
/// The five ProblemDetails <c>type</c> values AD-13 fixes. They are bare slugs — valid relative
/// URI-references under RFC 9457 §3.1, and exactly the strings the architecture records, so the
/// generated client and any integrator can switch on them without resolving a URL.
/// </summary>
public static class ProblemTypes
{
    public const string Validation = "validation";
    public const string Unauthorized = "unauthorized";
    public const string Forbidden = "forbidden";
    public const string NotFound = "not-found";
    public const string Conflict = "conflict";

    /// <summary>The slug for a status code, or <c>null</c> for a status AD-13 does not name.</summary>
    public static string? For(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => Validation,
        StatusCodes.Status401Unauthorized => Unauthorized,
        StatusCodes.Status403Forbidden => Forbidden,
        StatusCodes.Status404NotFound => NotFound,
        StatusCodes.Status409Conflict => Conflict,
        _ => null,
    };

    /// <summary>The user-facing sentence for a status. The web app shows it verbatim.</summary>
    public static string TitleFor(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "The request was not valid.",
        StatusCodes.Status401Unauthorized => "Authentication is required.",
        StatusCodes.Status403Forbidden => "You do not have permission to do that.",
        StatusCodes.Status404NotFound => "The resource was not found.",
        StatusCodes.Status409Conflict => "The request conflicts with the current state.",
        _ => "An error occurred.",
    };
}

/// <summary>
/// Turns the three exceptions that cross into the Api ring into their AD-13 status codes.
/// Anything else is left unhandled, so it becomes a 500 with no detail — an unrecognised
/// exception must never describe itself to a caller.
/// </summary>
internal sealed class ApiExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        (int status, string? detail) = exception switch
        {
            NotFoundException notFound => (StatusCodes.Status404NotFound, notFound.Message),
            DomainRuleException rule => (StatusCodes.Status409Conflict, rule.Message),
            ConcurrencyConflictException conflict => (StatusCodes.Status409Conflict, conflict.Message),
            _ => (0, null),
        };

        if (status == 0)
        {
            return false;
        }

        httpContext.Response.StatusCode = status;

        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Status = status,
                Detail = detail,
            },
        });
    }
}
