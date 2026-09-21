using ActionLedger.Web.Core;
using ActionLedger.Web.Core.Errors;
using Xunit;

namespace ActionLedger.Web.Tests;

/// <summary>
/// AD-14 — every shape the generated client throws stops here and becomes an
/// <see cref="ApiFailure"/>. All four shapes are real paths: a 400 is
/// <c>ApiException&lt;ValidationProblemDetails&gt;</c>, a 401 is
/// <c>ApiException&lt;ProblemDetails&gt;</c>, a 500 is a plain <c>ApiException</c> with no
/// problem body at all, and a transport failure never becomes an <c>ApiException</c>.
/// </summary>
public sealed class ApiFailuresTests
{
    [Fact]
    public void A_problem_details_failure_keeps_its_status_title_and_detail()
    {
        ApiFailure failure = ApiFailures.From(
            StubApiClient.Problem(401, "Authentication is required.", "The username or password is incorrect."));

        Assert.Equal(401, failure.StatusCode);
        Assert.Equal("Authentication is required.", failure.Title);
        Assert.Equal("The username or password is incorrect.", failure.Detail);
    }

    [Fact]
    public void A_validation_problem_is_mapped_too_although_it_is_not_a_problem_details_subclass()
    {
        // The generator emits ValidationProblemDetails as a separate flat class, so a single
        // ApiException<ProblemDetails> arm would miss every 400.
        ApiFailure failure = ApiFailures.From(StubApiClient.Invalid("The request was not valid."));

        Assert.Equal(400, failure.StatusCode);
        Assert.Equal("The request was not valid.", failure.Title);
    }

    [Fact]
    public void A_bare_api_exception_falls_back_to_the_unexpected_title()
    {
        // A 500 is the real case: the server sends neither type nor title, and the client raises
        // the non-generic ApiException. Without the fallback the load-failure sentence would read
        // "Couldn't load. " and stop.
        ApiFailure failure = ApiFailures.From(StubApiClient.Bare(500));

        Assert.Equal(500, failure.StatusCode);
        Assert.Equal(Voice.UnexpectedFailureTitle, failure.Title);
        Assert.Null(failure.Detail);
    }

    [Fact]
    public void An_undeclared_response_keeps_the_servers_own_problem_title()
    {
        // The generator only deserialises the responses an operation declared, so a 404 on an
        // operation that declared none arrives as a bare ApiException with the server's real
        // ProblemDetails sitting in Response as text. Discarding it would show Epic 2's first
        // list the generic fallback over a problem the server titled properly.
        ApiFailure failure = ApiFailures.From(StubApiClient.BareWithBody(
            404,
            """{"type":"not-found","title":"The resource was not found.","status":404,"detail":"No such meeting."}"""));

        Assert.Equal(404, failure.StatusCode);
        Assert.Equal("The resource was not found.", failure.Title);
        Assert.Equal("No such meeting.", failure.Detail);
    }

    [Theory]
    [InlineData("<html><body>502 Bad Gateway</body></html>")]
    [InlineData("{\"title\":\"\"}")]
    [InlineData("")]
    public void An_unreadable_or_untitled_body_still_falls_back(string body)
    {
        // A proxy's HTML error page is not JSON, and a truncated body is not a problem document.
        // Neither may throw out of the mapper.
        ApiFailure failure = ApiFailures.From(StubApiClient.BareWithBody(502, body));

        Assert.Equal(502, failure.StatusCode);
        Assert.Equal(Voice.UnexpectedFailureTitle, failure.Title);
    }

    [Fact]
    public void A_timeout_has_no_status_at_all()
    {
        // A timed-out request throws TaskCanceledException and never produced a response.
        ApiFailure failure = ApiFailures.From(new TaskCanceledException("the request timed out"));

        Assert.Equal(ApiFailures.NoResponse, failure.StatusCode);
        Assert.Equal(Voice.UnexpectedFailureTitle, failure.Title);
    }

    [Fact]
    public void A_transport_failure_has_no_status_at_all()
    {
        ApiFailure failure = ApiFailures.From(new HttpRequestException("no route to host"));

        Assert.Equal(ApiFailures.NoResponse, failure.StatusCode);
        Assert.Equal(Voice.UnexpectedFailureTitle, failure.Title);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_problem_body_with_no_usable_title_falls_back(string? title)
    {
        ApiFailure failure = ApiFailures.From(StubApiClient.Problem(409, title));

        Assert.Equal(409, failure.StatusCode);
        Assert.Equal(Voice.UnexpectedFailureTitle, failure.Title);
    }
}
