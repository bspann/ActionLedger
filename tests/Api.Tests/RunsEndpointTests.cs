using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ActionLedger.Api.Errors;
using ActionLedger.Application.Abstractions;
using ActionLedger.Application.Ai;
using ActionLedger.Domain.Actions;
using ActionLedger.Domain.Extraction;
using ActionLedger.Domain.Meetings;
using ActionLedger.Domain.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ActionLedger.Api.Tests;

/// <summary>
/// The run HTTP surface: 201 with a followed <c>Location</c> for Succeeded and Failed alike,
/// the 400 a meeting with no notes earns, the 404s, the full Run Detail read, and Meeting Detail's
/// run list.
/// </summary>
/// <remarks>
/// <para>
/// <c>TestApi</c>'s connection string points at nothing, so the persistence ports are replaced with
/// in-memory fakes and everything above them — routing, <c>ICurrentUser</c>, the ProblemDetails
/// mapping, the JSON options — stays exactly what the host wires up. The extractor is scripted too,
/// because the outcome is the subject: the real Fake provider always succeeds, so a Failed run
/// would be unreachable here.
/// </para>
/// <para>
/// <c>IExtractionSettings</c> is deliberately <em>not</em> replaced. It is the real implementation
/// over <c>TestApi.ValidConfiguration</c>'s <c>Ai:LowConfidenceThreshold</c>, so the flag below is
/// computed from configuration exactly as it is in the running api.
/// </para>
/// </remarks>
public sealed class RunsEndpointTests
{
    private const string Meetings = "/api/v1/meetings";

    private const string Runs = "/api/v1/runs";

    private static readonly ExtractionMetrics Metrics = new(
        "Fake",
        "fixture-catalog",
        "v1",
        "1",
        new DateTimeOffset(2026, 9, 22, 9, 15, 42, TimeSpan.Zero),
        DurationMs: 87,
        InputTokens: 0,
        OutputTokens: 0);

    private static readonly ExtractedProposal[] Kept =
    [
        new("Order the replacement scanners.", "Dana Whitfield", new DateOnly(2026, 9, 25), 0.91, "Dana will order the scanners."),
        new("Book the range.", "Facilities", null, 0.62, "Someone should book the range."),
    ];

    // ---------------------------------------------------------------------------------------
    // POST meetings/{id}/runs
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task A_succeeded_run_is_a_201_whose_location_is_the_run_detail_route_that_serves_it()
    {
        ExtractionStore store = new(ExtractionResult.Succeeded(Kept, [], Metrics, []));
        await using TestApi api = new() { ReplaceServices = store.Replace };
        using HttpClient client = api.CreateClientAs(nameof(Role.ActionOfficer));

        Guid meetingId = await MeetingWithNotesAsync(client);

        HttpResponseMessage response = await PostRunAsync(client, meetingId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        JsonElement run = await ReadAsync(response);
        Guid runId = run.GetProperty("id").GetGuid();

        Assert.NotEqual(Guid.Empty, runId);
        Assert.Equal("Succeeded", run.GetProperty("outcome").GetString());

        // The Location has to be usable, not merely present: CreatedAtAction resolves it across
        // controllers by action and controller name, so this is what proves those names still bind.
        Assert.NotNull(response.Headers.Location);
        Assert.Equal($"{Runs}/{runId}", response.Headers.Location!.AbsolutePath);

        HttpResponseMessage followed = await client.GetAsync(response.Headers.Location, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, followed.StatusCode);
        Assert.Equal(runId, (await ReadAsync(followed)).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task A_failed_run_is_also_a_201_and_the_location_still_resolves()
    {
        ExtractionStore store = new(ExtractionResult.Failed("Both attempts failed validation.", Metrics));
        await using TestApi api = new() { ReplaceServices = store.Replace };
        using HttpClient client = api.CreateClientAs(nameof(Role.ActionOfficer));

        Guid meetingId = await MeetingWithNotesAsync(client);

        HttpResponseMessage response = await PostRunAsync(client, meetingId);

        // AD-11 — a model that returned nonsense is not an HTTP error. This is the assertion the
        // whole "failed extraction is a 201" rule rests on at the boundary.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        JsonElement run = await ReadAsync(response);

        Assert.Equal("Failed", run.GetProperty("outcome").GetString());

        HttpResponseMessage followed = await client.GetAsync(response.Headers.Location, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, followed.StatusCode);

        JsonElement detail = await ReadAsync(followed);

        Assert.Equal("Both attempts failed validation.", detail.GetProperty("failureReason").GetString());
        Assert.Empty(detail.GetProperty("proposals").EnumerateArray());
    }

    [Fact]
    public async Task A_meeting_with_no_notes_is_a_400_validation_and_writes_no_run()
    {
        ExtractionStore store = new(ExtractionResult.Succeeded(Kept, [], Metrics, []));
        await using TestApi api = new() { ReplaceServices = store.Replace };
        using HttpClient client = api.CreateClientAs(nameof(Role.ActionOfficer));

        Guid meetingId = await MeetingAsync(client, "Meeting with nothing pasted", "2026-09-19");

        HttpResponseMessage response = await PostRunAsync(client, meetingId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        JsonElement problem = await ReadAsync(response);

        // Without the fourth arm in ApiExceptionHandler this is a 500 with no `type` at all.
        Assert.Equal(ProblemTypes.Validation, problem.GetProperty("type").GetString());
        Assert.Equal(StatusCodes.Status400BadRequest, problem.GetProperty("status").GetInt32());

        Assert.Empty(store.Runs);
    }

    /// <summary>
    /// The witness for the shape this route <em>publishes</em> for a 400. The no-notes 400 above is
    /// rendered by <c>ApiExceptionHandler</c> as a bare ProblemDetails, so it cannot prove the
    /// declared <c>ValidationProblemDetails</c>; only model binding produces the `errors` map the
    /// generated client is built to read.
    /// </summary>
    [Fact]
    public async Task A_malformed_meeting_id_on_the_run_route_is_a_400_validation_with_an_errors_map()
    {
        ExtractionStore store = new(ExtractionResult.Succeeded(Kept, [], Metrics, []));
        await using TestApi api = new() { ReplaceServices = store.Replace };
        using HttpClient client = api.CreateClientAs(nameof(Role.ActionOfficer));

        HttpResponseMessage response = await client.PostAsync(
            $"{Meetings}/not-a-guid/runs",
            content: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        JsonElement problem = await ReadAsync(response);

        Assert.Equal(ProblemTypes.Validation, problem.GetProperty("type").GetString());
        Assert.Equal(StatusCodes.Status400BadRequest, problem.GetProperty("status").GetInt32());

        // The member that makes this ValidationProblemDetails rather than ProblemDetails.
        Assert.True(problem.TryGetProperty("errors", out JsonElement errors));
        Assert.Equal(JsonValueKind.Object, errors.ValueKind);

        // Refused before the handler ran, so no provider call and no row.
        Assert.Empty(store.Runs);
    }

    [Fact]
    public async Task Starting_a_run_on_a_meeting_that_does_not_exist_is_a_404_not_found()
    {
        ExtractionStore store = new(ExtractionResult.Succeeded(Kept, [], Metrics, []));
        await using TestApi api = new() { ReplaceServices = store.Replace };
        using HttpClient client = api.CreateClientAs(nameof(Role.ActionOfficer));

        await AssertNotFoundAsync(await PostRunAsync(client, Guid.CreateVersion7()));
    }

    [Fact]
    public async Task A_meeting_may_have_any_number_of_runs()
    {
        ExtractionStore store = new(ExtractionResult.Succeeded(Kept, [], Metrics, []));
        await using TestApi api = new() { ReplaceServices = store.Replace };
        using HttpClient client = api.CreateClientAs(nameof(Role.ActionOfficer));

        Guid meetingId = await MeetingWithNotesAsync(client);

        Guid first = (await ReadAsync(await PostRunAsync(client, meetingId))).GetProperty("id").GetGuid();
        Guid second = (await ReadAsync(await PostRunAsync(client, meetingId))).GetProperty("id").GetGuid();

        Assert.NotEqual(first, second);

        // AD-5 — and the earlier run is still readable, with its own proposals.
        HttpResponseMessage earlier = await client.GetAsync($"{Runs}/{first}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, earlier.StatusCode);
        Assert.Equal(Kept.Length, (await ReadAsync(earlier)).GetProperty("proposals").GetArrayLength());
    }

    [Fact]
    public async Task The_meeting_list_reports_the_real_run_and_tracked_action_counts()
    {
        ExtractionStore store = new(ExtractionResult.Succeeded(Kept, [], Metrics, []));
        await using TestApi api = new() { ReplaceServices = store.Replace };
        using HttpClient client = api.CreateClientAs(nameof(Role.ActionOfficer));

        Guid busy = await MeetingWithNotesAsync(client);
        Guid quiet = await MeetingAsync(client, "Meeting nobody ran", "2026-09-17");

        Guid firstRun = (await ReadAsync(await PostRunAsync(client, busy))).GetProperty("id").GetGuid();
        await PostRunAsync(client, busy);

        // One proposal approved and one rejected: only the approval is a Tracked Action.
        JsonElement[] proposals =
        [
            .. (await ReadAsync(await client.GetAsync($"{Runs}/{firstRun}", TestContext.Current.CancellationToken)))
                .GetProperty("proposals")
                .EnumerateArray(),
        ];

        HttpResponseMessage approved = await client.PostAsJsonAsync(
            $"/api/v1/proposed-actions/{proposals[0].GetProperty("id").GetGuid()}/decision",
            new
            {
                decision = "Approve",
                description = proposals[0].GetProperty("description").GetString(),
                ownerUserId = (Guid?)null,
                dueDate = proposals[0].GetProperty("suggestedDueDate").GetString(),
            },
            TestContext.Current.CancellationToken);

        HttpResponseMessage rejected = await client.PostAsJsonAsync(
            $"/api/v1/proposed-actions/{proposals[1].GetProperty("id").GetGuid()}/decision",
            new { decision = "Reject" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);

        JsonElement page = await ReadAsync(await client.GetAsync(
            $"{Meetings}?page=1&pageSize=50",
            TestContext.Current.CancellationToken));

        JsonElement[] items = [.. page.GetProperty("items").EnumerateArray()];

        JsonElement busySummary = items.Single(meeting => meeting.GetProperty("id").GetGuid() == busy);
        JsonElement quietSummary = items.Single(meeting => meeting.GetProperty("id").GetGuid() == quiet);

        Assert.Equal(2, busySummary.GetProperty("runCount").GetInt32());
        Assert.Equal(0, quietSummary.GetProperty("runCount").GetInt32());

        // A correlated count through proposal and run, so the other Meeting stays at 0.
        Assert.Equal(1, busySummary.GetProperty("trackedActionCount").GetInt32());
        Assert.Equal(0, quietSummary.GetProperty("trackedActionCount").GetInt32());
    }

    // ---------------------------------------------------------------------------------------
    // GET runs/{id}
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Run_detail_publishes_every_FR6_field_with_tokens_rendered_as_zero()
    {
        ExtractionStore store = new(ExtractionResult.Succeeded(Kept, [], Metrics, ["Dropped: 'Nobody said this.'"]));
        await using TestApi api = new() { ReplaceServices = store.Replace };
        using HttpClient client = api.CreateClientAs(nameof(Role.ActionOfficer));

        Guid meetingId = await MeetingWithNotesAsync(client);
        Guid runId = (await ReadAsync(await PostRunAsync(client, meetingId))).GetProperty("id").GetGuid();

        JsonElement detail = await ReadAsync(await client.GetAsync($"{Runs}/{runId}", TestContext.Current.CancellationToken));

        Assert.Equal(runId, detail.GetProperty("id").GetGuid());
        Assert.Equal(meetingId, detail.GetProperty("meetingId").GetGuid());
        Assert.Equal(MeetingNotes.Sha256Length, detail.GetProperty("notesSha256").GetString()!.Length);
        Assert.NotEqual(Guid.Empty, detail.GetProperty("meetingNotesId").GetGuid());
        Assert.NotEqual(Guid.Empty, detail.GetProperty("startedByUserId").GetGuid());

        Assert.Equal("Fake", detail.GetProperty("provider").GetString());
        Assert.Equal("fixture-catalog", detail.GetProperty("model").GetString());
        Assert.Equal("v1", detail.GetProperty("promptVersion").GetString());
        Assert.Equal("1", detail.GetProperty("schemaVersion").GetString());
        Assert.Equal(87, detail.GetProperty("durationMs").GetInt32());

        // FR-6 — rendered as 0, not omitted and not null. Run Detail shows "0" rather than a blank.
        Assert.Equal(JsonValueKind.Number, detail.GetProperty("inputTokens").ValueKind);
        Assert.Equal(0, detail.GetProperty("inputTokens").GetInt32());
        Assert.Equal(0, detail.GetProperty("outputTokens").GetInt32());

        // Enums cross the wire as PascalCase strings (Consistency Conventions, Enums row).
        Assert.Equal("Succeeded", detail.GetProperty("outcome").GetString());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("failureReason").ValueKind);

        // Warnings are an array, present even when there is nothing to say.
        Assert.Equal(JsonValueKind.Array, detail.GetProperty("warnings").ValueKind);
        Assert.Equal("Dropped: 'Nobody said this.'", Assert.Single(detail.GetProperty("warnings").EnumerateArray()).GetString());

        // An instant is ISO 8601 UTC.
        Assert.StartsWith("2026-09-22T09:15:42", detail.GetProperty("startedAt").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_detail_publishes_its_proposals_in_ai_order_with_the_derived_values()
    {
        ExtractionStore store = new(ExtractionResult.Succeeded(Kept, [], Metrics, []));
        store.AddUser("Dana Whitfield");

        await using TestApi api = new() { ReplaceServices = store.Replace };
        using HttpClient client = api.CreateClientAs(nameof(Role.ActionOfficer));

        Guid meetingId = await MeetingWithNotesAsync(client);
        Guid runId = (await ReadAsync(await PostRunAsync(client, meetingId))).GetProperty("id").GetGuid();

        JsonElement[] proposals =
        [
            .. (await ReadAsync(await client.GetAsync($"{Runs}/{runId}", TestContext.Current.CancellationToken)))
                .GetProperty("proposals")
                .EnumerateArray(),
        ];

        Assert.Equal(Kept.Length, proposals.Length);
        Assert.Equal([0, 1], proposals.Select(proposal => proposal.GetProperty("ordinal").GetInt32()));
        Assert.Equal(
            [.. Kept.Select(proposal => proposal.Description)],
            proposals.Select(proposal => proposal.GetProperty("description").GetString()));

        // AD-15 — computed server-side against Ai:LowConfidenceThreshold (0.70 in this host).
        Assert.False(proposals[0].GetProperty("isLowConfidence").GetBoolean());
        Assert.True(proposals[1].GetProperty("isLowConfidence").GetBoolean());

        // AD-9 — a case-insensitive display-name match, or null; the free text survives either way.
        Assert.Equal(store.UserId("Dana Whitfield"), proposals[0].GetProperty("suggestedOwnerUserId").GetGuid());
        Assert.Equal("Dana Whitfield", proposals[0].GetProperty("suggestedOwner").GetString());

        Assert.Equal(JsonValueKind.Null, proposals[1].GetProperty("suggestedOwnerUserId").ValueKind);
        Assert.Equal("Facilities", proposals[1].GetProperty("suggestedOwner").GetString());

        // FR-10 — nothing in this story decides a proposal.
        Assert.All(proposals, proposal => Assert.Equal("Pending", proposal.GetProperty("reviewState").GetString()));

        // A stated date is a YYYY-MM-DD string; an unstated one is null, not a default date.
        Assert.Equal("2026-09-25", proposals[0].GetProperty("suggestedDueDate").GetString());
        Assert.Equal(JsonValueKind.Null, proposals[1].GetProperty("suggestedDueDate").ValueKind);
    }

    [Fact]
    public async Task The_low_confidence_flag_follows_a_non_default_configured_threshold()
    {
        ExtractionStore store = new(ExtractionResult.Succeeded(Kept, [], Metrics, []));

        // Every Ai:LowConfidenceThreshold in the repository is 0.70, so a hard-coded 0.70 in
        // Program.cs or ExtractionSettings leaves the whole suite green while an operator who sets
        // 0.85 silently gets 0.70. 0.95 flags both proposals; the shipped 0.70 flags only the
        // second, so this asserts the value travelled from configuration to the read model.
        await using TestApi api = new()
        {
            ReplaceServices = store.Replace,
            ConfigurationOverrides = { ["Ai:LowConfidenceThreshold"] = "0.95" },
        };

        using HttpClient client = api.CreateClientAs(nameof(Role.ActionOfficer));

        Guid meetingId = await MeetingWithNotesAsync(client);
        Guid runId = (await ReadAsync(await PostRunAsync(client, meetingId))).GetProperty("id").GetGuid();

        JsonElement[] proposals =
        [
            .. (await ReadAsync(await client.GetAsync($"{Runs}/{runId}", TestContext.Current.CancellationToken)))
                .GetProperty("proposals")
                .EnumerateArray(),
        ];

        // 0.91 is under 0.95 and over 0.70: it is flagged here and not in the default-threshold
        // test above, which is the whole difference the configured value makes.
        Assert.True(proposals[0].GetProperty("isLowConfidence").GetBoolean());
        Assert.True(proposals[1].GetProperty("isLowConfidence").GetBoolean());
    }

    [Fact]
    public async Task Reading_a_run_that_does_not_exist_is_a_404_not_found()
    {
        ExtractionStore store = new(ExtractionResult.Succeeded(Kept, [], Metrics, []));
        await using TestApi api = new() { ReplaceServices = store.Replace };
        using HttpClient client = api.CreateClientAs(nameof(Role.ActionOfficer));

        await AssertNotFoundAsync(await client.GetAsync(
            $"{Runs}/{Guid.CreateVersion7()}",
            TestContext.Current.CancellationToken));
    }

    // ---------------------------------------------------------------------------------------
    // GET meetings/{id}/runs
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task The_run_list_is_newest_first_by_start_time_with_both_counts_per_run()
    {
        ExtractedProposal[] three =
        [
            .. Kept,
            new("Send the floor plan.", "Marcus Bell", null, 0.8, "Marcus will send the floor plan."),
        ];

        // The first run started *later* than the second, so a list ordered by insertion or by the
        // time-ordered id would put them the other way round. Only StartedAt puts this one first.
        ExtractionResult succeeded = ExtractionResult.Succeeded(
            three,
            [],
            Metrics with { StartedAt = Metrics.StartedAt.AddMinutes(5) },
            []);

        ExtractionResult failed = ExtractionResult.Failed("The AI returned invalid output twice.", Metrics);

        ExtractionStore store = new(succeeded, failed);
        await using TestApi api = new() { ReplaceServices = store.Replace };
        using HttpClient client = api.CreateClientAs(nameof(Role.Lead));

        Guid meetingId = await MeetingWithNotesAsync(client);

        Guid succeededId = (await ReadAsync(await PostRunAsync(client, meetingId))).GetProperty("id").GetGuid();
        Guid failedId = (await ReadAsync(await PostRunAsync(client, meetingId))).GetProperty("id").GetGuid();

        HttpResponseMessage response = await client.GetAsync($"{Meetings}/{meetingId}/runs", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonElement[] runs = [.. (await ReadAsync(response)).EnumerateArray()];

        Assert.Equal([succeededId, failedId], runs.Select(run => run.GetProperty("id").GetGuid()));

        JsonElement newest = runs[0];

        Assert.StartsWith("2026-09-22T09:20:42", newest.GetProperty("startedAt").GetString()!, StringComparison.Ordinal);
        Assert.Equal("v1", newest.GetProperty("promptVersion").GetString());
        Assert.Equal("Fake", newest.GetProperty("provider").GetString());
        Assert.Equal("fixture-catalog", newest.GetProperty("model").GetString());
        Assert.Equal("Succeeded", newest.GetProperty("outcome").GetString());
        Assert.Equal(JsonValueKind.Null, newest.GetProperty("failureReason").ValueKind);
        Assert.Equal(3, newest.GetProperty("proposalCount").GetInt32());
        Assert.Equal(3, newest.GetProperty("pendingCount").GetInt32());

        JsonElement older = runs[1];

        Assert.Equal("Failed", older.GetProperty("outcome").GetString());
        Assert.Equal("The AI returned invalid output twice.", older.GetProperty("failureReason").GetString());
        Assert.Equal(0, older.GetProperty("proposalCount").GetInt32());
        Assert.Equal(0, older.GetProperty("pendingCount").GetInt32());
    }

    [Fact]
    public async Task The_run_list_counts_only_this_meetings_runs()
    {
        ExtractionStore store = new(ExtractionResult.Succeeded(Kept, [], Metrics, []));
        await using TestApi api = new() { ReplaceServices = store.Replace };
        using HttpClient client = api.CreateClientAs(nameof(Role.ActionOfficer));

        Guid mine = await MeetingWithNotesAsync(client);
        Guid other = await MeetingWithNotesAsync(client);

        Guid run = (await ReadAsync(await PostRunAsync(client, mine))).GetProperty("id").GetGuid();
        await PostRunAsync(client, other);

        JsonElement[] runs =
        [
            .. (await ReadAsync(await client.GetAsync($"{Meetings}/{mine}/runs", TestContext.Current.CancellationToken)))
                .EnumerateArray(),
        ];

        JsonElement single = Assert.Single(runs);

        Assert.Equal(run, single.GetProperty("id").GetGuid());
        Assert.Equal(Kept.Length, single.GetProperty("proposalCount").GetInt32());
    }

    [Fact]
    public async Task A_meeting_with_no_runs_lists_an_empty_array()
    {
        ExtractionStore store = new(ExtractionResult.Succeeded(Kept, [], Metrics, []));
        await using TestApi api = new() { ReplaceServices = store.Replace };
        using HttpClient client = api.CreateClientAs(nameof(Role.ActionOfficer));

        Guid meetingId = await MeetingAsync(client, "Meeting nobody ran", "2026-09-18");

        HttpResponseMessage response = await client.GetAsync($"{Meetings}/{meetingId}/runs", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonElement body = await ReadAsync(response);

        // An array, not null and not a 404: the meeting is there, it has simply not been run.
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
        Assert.Empty(body.EnumerateArray());
    }

    [Fact]
    public async Task Listing_the_runs_of_a_meeting_that_does_not_exist_is_a_404_not_found()
    {
        ExtractionStore store = new(ExtractionResult.Succeeded(Kept, [], Metrics, []));
        await using TestApi api = new() { ReplaceServices = store.Replace };
        using HttpClient client = api.CreateClientAs(nameof(Role.ActionOfficer));

        await AssertNotFoundAsync(await client.GetAsync(
            $"{Meetings}/{Guid.CreateVersion7()}/runs",
            TestContext.Current.CancellationToken));
    }

    // ---------------------------------------------------------------------------------------
    // Authorization
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("POST")]
    [InlineData("GET")]
    [InlineData("LIST")]
    public async Task An_anonymous_caller_is_401_before_any_meeting_or_run_is_loaded(string method)
    {
        ExtractionStore store = new(ExtractionResult.Succeeded(Kept, [], Metrics, []));
        await using TestApi api = new() { ReplaceServices = store.Replace };

        using HttpClient signedIn = api.CreateClientAs(nameof(Role.ActionOfficer));
        Guid meetingId = await MeetingWithNotesAsync(signedIn);

        using HttpClient anonymous = api.CreateClient();

        using HttpRequestMessage request = method switch
        {
            "POST" => new(HttpMethod.Post, $"{Meetings}/{meetingId}/runs"),
            "LIST" => new(HttpMethod.Get, $"{Meetings}/{meetingId}/runs"),
            _ => new(HttpMethod.Get, $"{Runs}/{Guid.CreateVersion7()}"),
        };

        HttpResponseMessage response = await anonymous.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        JsonElement problem = await ReadAsync(response);

        Assert.Equal(ProblemTypes.Unauthorized, problem.GetProperty("type").GetString());

        // Authorization runs before the endpoint, so the meeting that really is there was never
        // loaded and no run was written for it.
        Assert.Empty(store.Runs);
    }

    // ---------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------

    private static Task<HttpResponseMessage> PostRunAsync(HttpClient client, Guid meetingId) =>
        // No body at all: the actor is the token's subject and the provider is configuration.
        client.PostAsync($"{Meetings}/{meetingId}/runs", content: null, TestContext.Current.CancellationToken);

    private static async Task<Guid> MeetingAsync(HttpClient client, string title, string held)
    {
        HttpResponseMessage created = await client.PostAsJsonAsync(
            Meetings,
            new { title, meetingDate = held, attendees = Array.Empty<string>() },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        return (await ReadAsync(created)).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> MeetingWithNotesAsync(HttpClient client)
    {
        Guid id = await MeetingAsync(client, $"Weekly sync {Guid.CreateVersion7():N}", "2026-09-22");

        HttpResponseMessage saved = await client.PutAsJsonAsync(
            $"{Meetings}/{id}/notes",
            new { text = "Dana will order the scanners. Someone should book the range." },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        return id;
    }

    private static async Task AssertNotFoundAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        JsonElement problem = await ReadAsync(response);

        Assert.Equal(ProblemTypes.NotFound, problem.GetProperty("type").GetString());
        Assert.Equal(StatusCodes.Status404NotFound, problem.GetProperty("status").GetInt32());
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        JsonDocument
            .Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .RootElement
            .Clone();

    /// <summary>
    /// The persistence ports and the extractor, in memory. Nothing here interprets a request: the
    /// store holds whatever the aggregate produced, so the assertions above are about the Api ring.
    /// </summary>
    private sealed class ExtractionStore(params ExtractionResult[] results)
        : IMeetingRepository, IExtractionRunRepository, IActionRepository, IActionRevisionRepository, IUnitOfWork, IReadDb, IActionExtractor
    {
        private int _extractions;

        private readonly List<object> _pending = [];
        private readonly List<Meeting> _meetings = [];
        private readonly List<ExtractionRun> _runs = [];
        private readonly List<ActionRevision> _revisions = [];
        private readonly List<TrackedAction> _trackedActions = [];
        private readonly List<User> _users = [];

        /// <summary>The runs that were committed, so a refused request can be shown to have written none.</summary>
        public IReadOnlyList<ExtractionRun> Runs => _runs;

        public void AddUser(string displayName) =>
            _users.Add(User.Register(
                displayName.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant(),
                displayName,
                new string('h', 60),
                Role.ActionOfficer,
                DateTimeOffset.UnixEpoch));

        public Guid UserId(string displayName) => _users.Single(user => user.DisplayName == displayName).Id;

        public void Replace(IServiceCollection services)
        {
            services.RemoveAll<IMeetingRepository>();
            services.RemoveAll<IExtractionRunRepository>();
            services.RemoveAll<IActionRepository>();
            services.RemoveAll<IActionRevisionRepository>();
            services.RemoveAll<IUnitOfWork>();
            services.RemoveAll<IReadDb>();

            // The real Fake provider always succeeds, so a Failed run would be unreachable through
            // it. Everything else about the seam — including IExtractionSettings — stays real.
            services.RemoveAll<IActionExtractor>();

            services.AddSingleton<IMeetingRepository>(this);
            services.AddSingleton<IExtractionRunRepository>(this);
            services.AddSingleton<IActionRepository>(this);
            services.AddSingleton<IActionRevisionRepository>(this);
            services.AddSingleton<IUnitOfWork>(this);
            services.AddSingleton<IReadDb>(this);
            services.AddSingleton<IActionExtractor>(this);
        }

        public void Add(Meeting meeting) => _pending.Add(meeting);

        public void Add(ExtractionRun run) => _pending.Add(run);

        public void Add(TrackedAction trackedAction) => _pending.Add(trackedAction);

        public void AddRange(IReadOnlyList<ActionRevision> revisions) => _pending.AddRange(revisions);

        public Task<Meeting?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_meetings.Find(meeting => meeting.Id == id));

        Task<ExtractionRun?> IExtractionRunRepository.FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_runs.Find(run => run.Id == id));

        public Task<ExtractionRun?> FindByProposedActionIdAsync(Guid proposedActionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_runs.Find(run => run.Proposals.Any(proposal => proposal.Id == proposedActionId)));

        public Task<IReadOnlyList<ActionRevision>> ListForTargetAsync(
            RevisionTargetType targetType,
            Guid targetId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActionRevision>>(
            [
                .. _revisions
                    .Where(revision => revision.TargetType == targetType && revision.TargetId == targetId)
                    .OrderBy(revision => revision.OccurredAt)
                    .ThenBy(revision => revision.Sequence),
            ]);

        /// <summary>
        /// The scripted results in order, one per run; the last one answers every run after it.
        /// </summary>
        public Task<ExtractionResult> ExtractAsync(ExtractionRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(results[Math.Min(_extractions++, results.Length - 1)]);

        public Task<int> CommitAsync(CancellationToken cancellationToken = default)
        {
            int written = _pending.Count;

            foreach (object staged in _pending)
            {
                switch (staged)
                {
                    case Meeting meeting:
                        _meetings.Add(meeting);
                        break;
                    case ExtractionRun run:
                        _runs.Add(run);
                        break;
                    case ActionRevision revision:
                        _revisions.Add(revision);
                        break;
                    case TrackedAction trackedAction:
                        _trackedActions.Add(trackedAction);
                        break;
                }
            }

            _pending.Clear();

            return Task.FromResult(written);
        }

        public IQueryable<T> Query<T>()
            where T : class =>
            typeof(T) == typeof(ProposedAction)
                // Proposals have no list of their own here: they live on the run that minted them,
                // exactly as the model has them living on a cascade from extraction_runs.
                ? _runs.SelectMany(run => run.Proposals).Cast<T>().AsQueryable()
                : _meetings.Cast<object>()
                    .Concat(_runs)
                    .Concat(_revisions)
                    .Concat(_trackedActions)
                    .Concat(_users)
                    .OfType<T>()
                    .AsQueryable();

        public Task<IReadOnlyList<T>> ToListAsync<T>(IQueryable<T> query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<T>>([.. query]);

        public Task<int> CountAsync<T>(IQueryable<T> query, CancellationToken cancellationToken = default) =>
            Task.FromResult(query.Count());
    }
}
