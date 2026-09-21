using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ActionLedger.Api.Errors;
using ActionLedger.Api.Observability;
using Xunit;

namespace ActionLedger.Api.Tests;

/// <summary>
/// One test per error row of the story's I/O matrix, against the real middleware pipeline.
/// AD-13 fixes both the status and the <c>type</c> slug; a change to either is a contract change.
/// </summary>
public sealed class ProblemDetailsTests
{
    private static TestApi WithProbes() => new() { IncludeTestEndpoints = true };

    [Fact]
    public async Task Domain_rule_violation_is_a_409_conflict()
    {
        await using TestApi api = WithProbes();
        using HttpClient client = api.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/errors/domain-rule", TestContext.Current.CancellationToken);

        await AssertProblem(response, HttpStatusCode.Conflict, ProblemTypes.Conflict);
    }

    [Fact]
    public async Task Concurrency_conflict_is_a_409_conflict()
    {
        await using TestApi api = WithProbes();
        using HttpClient client = api.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/errors/concurrency", TestContext.Current.CancellationToken);

        await AssertProblem(response, HttpStatusCode.Conflict, ProblemTypes.Conflict);
    }

    [Fact]
    public async Task Missing_resource_is_a_404_not_found()
    {
        await using TestApi api = WithProbes();
        using HttpClient client = api.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/errors/not-found", TestContext.Current.CancellationToken);

        await AssertProblem(response, HttpStatusCode.NotFound, ProblemTypes.NotFound);
    }

    [Fact]
    public async Task Bad_payload_is_a_400_validation_with_per_field_errors()
    {
        await using TestApi api = WithProbes();
        using HttpClient client = api.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/errors/validate",
            new { description = "no", priority = 9 },
            TestContext.Current.CancellationToken);

        JsonElement problem = await AssertProblem(response, HttpStatusCode.BadRequest, ProblemTypes.Validation);

        JsonElement errors = problem.GetProperty("errors");

        Assert.Equal(
            "A description needs at least 3 characters.",
            errors.GetProperty("Description")[0].GetString());

        Assert.Equal(
            "Priority runs from 1 to 5.",
            errors.GetProperty("Priority")[0].GetString());
    }

    [Fact]
    public async Task No_credential_on_a_protected_route_is_a_401_unauthorized()
    {
        await using TestApi api = WithProbes();
        using HttpClient client = api.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/protected/any", TestContext.Current.CancellationToken);

        await AssertProblem(response, HttpStatusCode.Unauthorized, ProblemTypes.Unauthorized);
    }

    [Fact]
    public async Task An_invalid_token_is_a_401_unauthorized()
    {
        await using TestApi api = WithProbes();
        using HttpClient client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "not.a.token");

        HttpResponseMessage response = await client.GetAsync("/api/v1/protected/any", TestContext.Current.CancellationToken);

        await AssertProblem(response, HttpStatusCode.Unauthorized, ProblemTypes.Unauthorized);
    }

    [Fact]
    public async Task An_authenticated_user_in_the_wrong_role_is_a_403_forbidden()
    {
        await using TestApi api = WithProbes();
        using HttpClient client = api.CreateClientAs("ActionOfficer");

        HttpResponseMessage response = await client.GetAsync("/api/v1/protected/lead-only", TestContext.Current.CancellationToken);

        await AssertProblem(response, HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
    }

    [Fact]
    public async Task An_authenticated_user_in_the_right_role_is_allowed_through()
    {
        await using TestApi api = WithProbes();
        using HttpClient client = api.CreateClientAs("Lead");

        HttpResponseMessage response = await client.GetAsync("/api/v1/protected/lead-only", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_unmapped_path_is_a_ProblemDetails_404_not_an_html_error_page()
    {
        await using TestApi api = new();
        using HttpClient client = api.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/nothing-here", TestContext.Current.CancellationToken);

        await AssertProblem(response, HttpStatusCode.NotFound, ProblemTypes.NotFound);
    }

    [Fact]
    public async Task An_unmapped_exception_is_a_500_that_says_nothing_about_itself()
    {
        await using TestApi api = WithProbes();
        using HttpClient client = api.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/errors/unexpected", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("Connection string leaked", body, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_problem_carries_the_correlation_id_of_the_request_that_produced_it()
    {
        await using TestApi api = WithProbes();
        using HttpClient client = api.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/errors/not-found", TestContext.Current.CancellationToken);

        JsonElement problem = await AssertProblem(response, HttpStatusCode.NotFound, ProblemTypes.NotFound);

        Assert.False(
            string.IsNullOrWhiteSpace(problem.GetProperty(RequestCorrelation.PropertyName).GetString()));
    }

    /// <summary>Asserts the wire shape AD-13 fixes: the status, the media type, and the <c>type</c> slug.</summary>
    private static async Task<JsonElement> AssertProblem(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedType)
    {
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expectedStatus, response.StatusCode);

        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);

        JsonElement problem = JsonDocument.Parse(body).RootElement;

        Assert.Equal(expectedType, problem.GetProperty("type").GetString());
        Assert.Equal((int)expectedStatus, problem.GetProperty("status").GetInt32());
        // The title is user-facing: the web app renders it verbatim in its load-failure and
        // write-failure states, so it is part of the contract, not decoration.
        Assert.Equal(ProblemTypes.TitleFor((int)expectedStatus), problem.GetProperty("title").GetString());

        // One correlation field, not the framework's `traceId` alongside it.
        Assert.False(problem.TryGetProperty("traceId", out _));

        return problem.Clone();
    }
}
