using System.Text.Json;
using ActionLedger.Web.Core.Api;

namespace ActionLedger.Web.Core.Errors;

/// <summary>
/// AD-14 — turns every shape the generated client throws into an <see cref="ApiFailure"/>, so the
/// generated exception types stop at the seam.
/// </summary>
/// <remarks>
/// <para>
/// The client throws four different things and all four are real paths. A 400 is
/// <c>ApiException&lt;ValidationProblemDetails&gt;</c>, a 401 is
/// <c>ApiException&lt;ProblemDetails&gt;</c>, anything the calling operation did not declare —
/// including a 500 — is a plain non-generic <c>ApiException</c>, and a transport failure or a
/// timeout never becomes an <c>ApiException</c> in the first place.
/// <c>ValidationProblemDetails</c> is a separate flat class rather than a subclass of
/// <c>ProblemDetails</c>, so it needs its own arm; dispatch is on the status code, never on
/// <c>type</c>, because a bare 500 carries neither.
/// </para>
/// <para>
/// A plain <c>ApiException</c> does not mean a bodyless failure. The generator only deserialises
/// the responses the operation declared, so a 404 or a 409 on an operation that declared neither
/// arrives here undeserialised — with the server's real ProblemDetails sitting in
/// <c>Response</c> as text. Reading it is what stops Epic 2's first list from rendering
/// "Couldn't load. An unexpected error occurred." over a problem the server titled properly.
/// </para>
/// </remarks>
public static class ApiFailures
{
    /// <summary>The status used when the request never produced a response.</summary>
    public const int NoResponse = 0;

    public static ApiFailure From(Exception exception) => exception switch
    {
        ApiException<ProblemDetails> problem =>
            new ApiFailure(problem.StatusCode, TitleOrFallback(problem.Result?.Title), problem.Result?.Detail),
        ApiException<ValidationProblemDetails> invalid =>
            new ApiFailure(invalid.StatusCode, TitleOrFallback(invalid.Result?.Title), invalid.Result?.Detail),
        ApiException api => FromUndeclaredResponse(api),

        // Everything else: transport failures, and the TaskCanceledException a timeout or a
        // cancellation arrives as. They are all the same to a caller — no response arrived, so
        // there is no status and nothing of the server's to render. Kept as one arm because a
        // separate arm per exception type would produce this identical value.
        _ => new ApiFailure(NoResponse, Voice.UnexpectedFailureTitle, null),
    };

    private static ApiFailure FromUndeclaredResponse(ApiException exception)
    {
        ProblemDetails? problem = ReadProblem(exception.Response);

        return new ApiFailure(exception.StatusCode, TitleOrFallback(problem?.Title), problem?.Detail);
    }

    private static ProblemDetails? ReadProblem(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ProblemDetails>(response);
        }
        catch (JsonException)
        {
            // A proxy's HTML error page, or a truncated body. The fallback title covers it.
            return null;
        }
    }

    private static string TitleOrFallback(string? title) =>
        string.IsNullOrWhiteSpace(title) ? Voice.UnexpectedFailureTitle : title;
}
