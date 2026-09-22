using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ActionLedger.Api.Errors;
using ActionLedger.Api.Routing;
using ActionLedger.Application.Meetings;
using ActionLedger.Domain.Meetings;
using ActionLedger.Domain.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ActionLedger.Api.Tests;

/// <summary>
/// AD-13 — the rows of the story's I/O matrix that are decided above persistence: the four routes,
/// the validation 400s, and the <c>type</c> slug on each failure.
/// </summary>
/// <remarks>
/// <c>TestApi</c>'s connection string points at a host with no database behind it, so anything
/// that reaches a repository or a query cannot be exercised here — that is
/// <c>Infrastructure.Tests</c>' half. What is asserted here is what model binding and model
/// validation decide before a handler ever runs, plus the routes themselves.
/// </remarks>
public sealed class MeetingsEndpointTests
{
    private const string Meetings = "/api/v1/meetings";

    private static readonly string NotesRoute = $"{Meetings}/{Guid.CreateVersion7()}/notes";

    private static HttpClient SignedIn(TestApi api) => api.CreateClientAs(nameof(Role.ActionOfficer));

    [Fact]
    public async Task The_meeting_routes_are_mapped_under_the_version_prefix()
    {
        await using TestApi api = new();
        using HttpClient _ = api.CreateClient();

        string[] routes =
        [
            .. api.Services.GetRequiredService<EndpointDataSource>()
                .Endpoints
                .OfType<RouteEndpoint>()
                .Select(endpoint => "/" + endpoint.RoutePattern.RawText?.TrimStart('/'))
                .Where(route => route.Contains("meetings", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];

        Assert.Equal(
            [
                $"/{ApiRoutes.Prefix}/meetings",
                $"/{ApiRoutes.Prefix}/meetings/{{id}}",
                $"/{ApiRoutes.Prefix}/meetings/{{id}}/notes",
                // AD-13 — a Meeting's runs are created and listed here and each one is read at
                // runs/{id}, so the POST and the list GET share this one pattern.
                $"/{ApiRoutes.Prefix}/meetings/{{id}}/runs",
            ],
            routes);
    }

    [Theory]
    [InlineData(Malformation.BlankTitle)]
    [InlineData(Malformation.WhitespaceTitle)]
    [InlineData(Malformation.OverlongTitle)]
    [InlineData(Malformation.MissingTitle)]
    [InlineData(Malformation.MissingDate)]
    [InlineData(Malformation.OverlongAttendee)]
    [InlineData(Malformation.BlankAttendee)]
    public async Task A_malformed_create_is_a_400_validation_before_the_handler_runs(Malformation malformation)
    {
        await using TestApi api = new();
        using HttpClient client = SignedIn(api);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            Meetings,
            Body(malformation),
            TestContext.Current.CancellationToken);

        await AssertProblem(response, HttpStatusCode.BadRequest, ProblemTypes.Validation);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    // MinimumLength = 1 already refuses the two above; these reach only [Required], which is the
    // sole guard against a paste of nothing but whitespace becoming permanent write-once notes.
    // The Domain deliberately stores whitespace verbatim, so nothing below this refuses it.
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public async Task Notes_that_are_absent_empty_or_only_whitespace_are_a_400_validation(string? text)
    {
        await using TestApi api = new();
        using HttpClient client = SignedIn(api);

        HttpResponseMessage response = await client.PutAsJsonAsync(
            NotesRoute,
            new { text },
            TestContext.Current.CancellationToken);

        await AssertProblem(response, HttpStatusCode.BadRequest, ProblemTypes.Validation);
    }

    [Fact]
    public async Task Notes_one_character_over_the_limit_are_a_400_validation()
    {
        await using TestApi api = new();
        using HttpClient client = SignedIn(api);

        HttpResponseMessage response = await client.PutAsJsonAsync(
            NotesRoute,
            new { text = new string('n', MeetingNotes.TextMaxLength + 1) },
            TestContext.Current.CancellationToken);

        await AssertProblem(response, HttpStatusCode.BadRequest, ProblemTypes.Validation);
    }

    [Fact]
    public async Task A_malformed_meeting_id_is_a_400_validation_rather_than_a_404()
    {
        await using TestApi api = new();
        using HttpClient client = SignedIn(api);

        HttpResponseMessage response = await client.GetAsync(
            $"{Meetings}/not-a-guid",
            TestContext.Current.CancellationToken);

        await AssertProblem(response, HttpStatusCode.BadRequest, ProblemTypes.Validation);
    }

    [Theory]
    [InlineData("GET", "")]
    [InlineData("POST", "")]
    [InlineData("GET", "/00000000-0000-0000-0000-000000000000")]
    [InlineData("PUT", "/00000000-0000-0000-0000-000000000000/notes")]
    [InlineData("GET", "/00000000-0000-0000-0000-000000000000/runs")]
    public async Task Every_meeting_operation_refuses_an_anonymous_caller(string method, string suffix)
    {
        await using TestApi api = new();
        using HttpClient client = api.CreateClient();

        using HttpRequestMessage request = new(new HttpMethod(method), Meetings + suffix);

        if (method is "POST" or "PUT")
        {
            request.Content = JsonContent.Create(new { });
        }

        HttpResponseMessage response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        await AssertProblem(response, HttpStatusCode.Unauthorized, ProblemTypes.Unauthorized);
    }

    [Fact]
    public void A_bare_Authorize_is_the_whole_write_rule_only_while_every_role_is_a_writer()
    {
        // MeetingsController deliberately carries no role filter, on the stated grounds that both
        // Role members may write. That premise is what makes the absence correct, and until now it
        // lived only in a comment. A third role would silently gain every Meeting write, so it has
        // to arrive through this assertion: either it is a writer and belongs on the list, or the
        // controller needs the policy the comment says it does not.
        Assert.Equal(
            [nameof(Role.ActionOfficer), nameof(Role.Lead)],
            Enum.GetNames<Role>().Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// The ways a create request can be wrong. Derived from the Domain's own constants rather than
    /// from literals, so raising a limit cannot leave a theory asserting the old one.
    /// </summary>
    public enum Malformation
    {
        BlankTitle,
        WhitespaceTitle,
        OverlongTitle,
        MissingTitle,
        MissingDate,
        OverlongAttendee,
        BlankAttendee,
    }

    private static object Body(Malformation malformation) => malformation switch
    {
        Malformation.BlankTitle => new { title = string.Empty, meetingDate = "2026-09-18", attendees = Array.Empty<string>() },
        Malformation.WhitespaceTitle => new { title = "   ", meetingDate = "2026-09-18", attendees = Array.Empty<string>() },
        Malformation.OverlongTitle => new { title = new string('t', Meeting.TitleMaxLength + 1), meetingDate = "2026-09-18", attendees = Array.Empty<string>() },
        Malformation.MissingTitle => new { meetingDate = "2026-09-18", attendees = Array.Empty<string>() },
        Malformation.MissingDate => new { title = "Weekly sync", attendees = Array.Empty<string>() },
        Malformation.OverlongAttendee => new { title = "Weekly sync", meetingDate = "2026-09-18", attendees = new[] { new string('a', Meeting.AttendeeMaxLength + 1) } },
        Malformation.BlankAttendee => new { title = "Weekly sync", meetingDate = "2026-09-18", attendees = new[] { "  " } },
        _ => throw new ArgumentOutOfRangeException(nameof(malformation)),
    };

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string type)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        JsonElement problem = JsonDocument
            .Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .RootElement;

        Assert.Equal(type, problem.GetProperty("type").GetString());
        Assert.Equal((int)status, problem.GetProperty("status").GetInt32());
    }
}
