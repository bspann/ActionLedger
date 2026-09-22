using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ActionLedger.Api.Errors;
using ActionLedger.Api.OpenApi;
using ActionLedger.Application.Abstractions;
using ActionLedger.Domain.Actions;
using ActionLedger.Domain.Extraction;
using ActionLedger.Domain.Meetings;
using ActionLedger.Domain.Users;
using ActionLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ActionLedger.Api.Tests;

/// <summary>
/// AD-3, AD-9, AD-12, AD-13 and AD-20 for <c>POST /api/v1/proposed-actions/{id}/decision</c>, end
/// to end against a real <c>postgres:18-alpine</c>: every verb's 200, every refusal's
/// ProblemDetails, and the race two reviewers run when they decide one proposal at once.
/// </summary>
/// <remarks>
/// The runs and proposals are written straight through the repositories, the way
/// <c>TrackedActionPersistenceTests</c> writes them, because the subject here is the decision and
/// not the extractor. Each test builds its own Meeting, so the shared database never couples two
/// tests' rows.
/// </remarks>
public sealed class DecisionEndpointTests(SeededApi seeded) : IClassFixture<SeededApi>
{
    private static readonly DateOnly Held = new(2026, 9, 22);

    private static readonly DateOnly Proposed = new(2026, 9, 25);

    private static readonly DateTimeOffset Created = new(2026, 9, 22, 14, 30, 0, TimeSpan.Zero);

    private static readonly ExtractionRunMetadata Metrics = new(
        "Fake", "fixture-catalog", "v1", "1", Created, DurationMs: 87, InputTokens: 0, OutputTokens: 0);

    private const string Description = "Order the replacement scanners.";

    private static string DecisionPath(Guid id) => $"/api/v1/proposed-actions/{id}/decision";

    // ---------------------------------------------------------------------------------------
    // 200s
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Approving_as_proposed_is_approved_and_owned_by_the_sent_owner()
    {
        Guid dana = await UserIdAsync(SeededApi.SomeoneWhoSignsIn.DisplayName);
        Guid actor = await UserIdAsync("Marcus Bell");
        Guid proposal = await SavedProposalAsync(SeededApi.SomeoneWhoSignsIn.DisplayName);
        using HttpClient client = ClientAs(actor);

        HttpResponseMessage response = await PostAsync(client, proposal, Approve(Description, dana, "2026-09-25"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonElement answer = await ReadAsync(response);

        Assert.Equal(proposal, answer.GetProperty("proposedActionId").GetGuid());
        Assert.Equal("Approved", answer.GetProperty("reviewState").GetString());
        Assert.Equal(actor, answer.GetProperty("decidedByUserId").GetGuid());
        Assert.Equal(TimeSpan.Zero, answer.GetProperty("decidedAt").GetDateTimeOffset().Offset);

        Guid trackedActionId = answer.GetProperty("trackedActionId").GetGuid();
        TrackedAction stored = await TrackedActionAsync(proposal);

        Assert.Equal(trackedActionId, stored.Id);
        Assert.Equal(dana, stored.OwnerUserId);
        Assert.Equal(Proposed, stored.DueDate);
        Assert.Equal(1, await RevisionCountAsync(proposal, stored.Id));
    }

    [Fact]
    public async Task Keeping_an_unmatched_owner_unassigned_is_approved()
    {
        Guid proposal = await SavedProposalAsync("Facilities");
        using HttpClient client = ClientAs(await UserIdAsync("Marcus Bell"));

        JsonElement answer = await ReadAsync(await PostAsync(client, proposal, Approve(Description, null, "2026-09-25")));

        Assert.Equal("Approved", answer.GetProperty("reviewState").GetString());
        Assert.Null((await TrackedActionAsync(proposal)).OwnerUserId);
    }

    [Fact]
    public async Task A_changed_due_date_is_an_edit_with_the_edited_value_tracked()
    {
        Guid dana = await UserIdAsync(SeededApi.SomeoneWhoSignsIn.DisplayName);
        Guid proposal = await SavedProposalAsync(SeededApi.SomeoneWhoSignsIn.DisplayName);
        using HttpClient client = ClientAs(dana);

        JsonElement answer = await ReadAsync(await PostAsync(client, proposal, Approve(Description, dana, "2026-10-02")));

        Assert.Equal("Edited", answer.GetProperty("reviewState").GetString());

        TrackedAction stored = await TrackedActionAsync(proposal);

        Assert.Equal(new DateOnly(2026, 10, 2), stored.DueDate);

        // ReviewDecision on the proposal, then one FieldEdit on the Tracked Action.
        Assert.Equal(2, await RevisionCountAsync(proposal, stored.Id));
    }

    [Fact]
    public async Task Clearing_a_matched_owner_is_an_edit_that_leaves_the_action_unassigned()
    {
        Guid proposal = await SavedProposalAsync(SeededApi.SomeoneWhoSignsIn.DisplayName);
        using HttpClient client = ClientAs(await UserIdAsync("Marcus Bell"));

        JsonElement answer = await ReadAsync(await PostAsync(client, proposal, Approve(Description, null, "2026-09-25")));

        Assert.Equal("Edited", answer.GetProperty("reviewState").GetString());

        // AD-9 — never the match.
        Assert.Null((await TrackedActionAsync(proposal)).OwnerUserId);
    }

    [Fact]
    public async Task A_rejection_creates_no_action_and_records_the_trimmed_reason()
    {
        Guid proposal = await SavedProposalAsync(SeededApi.SomeoneWhoSignsIn.DisplayName);
        using HttpClient client = ClientAs(await UserIdAsync("Marcus Bell"));

        HttpResponseMessage response = await PostAsync(client, proposal, new { decision = "Reject", reason = "  dup " });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonElement answer = await ReadAsync(response);

        Assert.Equal("Rejected", answer.GetProperty("reviewState").GetString());
        Assert.Equal(JsonValueKind.Null, answer.GetProperty("trackedActionId").ValueKind);

        ProposedAction stored = await ProposalAsync(proposal);

        Assert.Equal(ReviewState.Rejected, stored.ReviewState);
        Assert.Equal("dup", stored.RejectionReason);
        Assert.Equal(0, await TrackedActionCountAsync(proposal));
    }

    // ---------------------------------------------------------------------------------------
    // Refusals
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task An_unknown_proposal_is_not_found()
    {
        using HttpClient client = ClientAs(await UserIdAsync("Marcus Bell"));

        HttpResponseMessage response = await PostAsync(client, Guid.CreateVersion7(), Approve(Description, null, null));

        await AssertProblemAsync(response, HttpStatusCode.NotFound, ProblemTypes.NotFound);
    }

    public enum BadInput
    {
        BlankDescription,
        OverlongDescription,
        ReasonOnApproval,
        UnknownOwner,
        SystemOwner,
        OverlongReason,
        MissingDecision,
        UnknownDecision,
        ClientSuppliedKind,
    }

    [Theory]
    [InlineData(BadInput.BlankDescription)]
    [InlineData(BadInput.OverlongDescription)]
    [InlineData(BadInput.ReasonOnApproval)]
    [InlineData(BadInput.UnknownOwner)]
    [InlineData(BadInput.SystemOwner)]
    [InlineData(BadInput.OverlongReason)]
    [InlineData(BadInput.MissingDecision)]
    [InlineData(BadInput.UnknownDecision)]
    [InlineData(BadInput.ClientSuppliedKind)]
    public async Task Bad_input_is_a_validation_problem_that_writes_nothing(BadInput bad)
    {
        Guid proposal = await SavedProposalAsync(SeededApi.SomeoneWhoSignsIn.DisplayName);
        using HttpClient client = ClientAs(await UserIdAsync("Marcus Bell"));

        object body = bad switch
        {
            BadInput.BlankDescription => Approve("   ", null, null),
            BadInput.OverlongDescription => Approve(new string('d', ProposedAction.DescriptionMaxLength + 1), null, null),
            BadInput.ReasonOnApproval => new { decision = "Approve", description = Description, reason = "Because." },
            BadInput.UnknownOwner => Approve(Description, Guid.CreateVersion7(), null),
            BadInput.SystemOwner => Approve(Description, await SystemUserIdAsync(), null),
            BadInput.OverlongReason => new { decision = "Reject", reason = new string('r', ProposedAction.RejectionReasonMaxLength + 1) },
            BadInput.MissingDecision => new { description = Description },
            BadInput.UnknownDecision => new { decision = "Maybe", description = Description },
            // The kind is the server's to choose (AD-3), so it is not a verb the client can send.
            BadInput.ClientSuppliedKind => new { decision = "Edited", description = Description },
            _ => throw new ArgumentOutOfRangeException(nameof(bad)),
        };

        HttpResponseMessage response = await PostAsync(client, proposal, body);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, ProblemTypes.Validation);

        Assert.Equal(ReviewState.Pending, (await ProposalAsync(proposal)).ReviewState);
        Assert.Equal(0, await TrackedActionCountAsync(proposal));
    }

    [Fact]
    public async Task An_anonymous_caller_is_unauthorized()
    {
        Guid proposal = await SavedProposalAsync(SeededApi.SomeoneWhoSignsIn.DisplayName);
        using HttpClient client = seeded.Api.CreateClient();

        HttpResponseMessage response = await PostAsync(client, proposal, Approve(Description, null, null));

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, ProblemTypes.Unauthorized);
        Assert.Equal(ReviewState.Pending, (await ProposalAsync(proposal)).ReviewState);
    }

    [Theory]
    [InlineData("Approve")]
    [InlineData("Reject")]
    public async Task Deciding_a_decided_proposal_is_a_conflict_that_writes_nothing(string verb)
    {
        Guid proposal = await SavedProposalAsync("Facilities");
        using HttpClient client = ClientAs(await UserIdAsync("Marcus Bell"));

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(client, proposal, new { decision = "Reject" })).StatusCode);

        HttpResponseMessage response = await PostAsync(client, proposal, new { decision = verb, description = Description });

        await AssertProblemAsync(response, HttpStatusCode.Conflict, ProblemTypes.Conflict);

        Assert.Equal(ReviewState.Rejected, (await ProposalAsync(proposal)).ReviewState);
        Assert.Equal(0, await TrackedActionCountAsync(proposal));
    }

    // ---------------------------------------------------------------------------------------
    // The race
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Two_decisions_sent_at_once_are_one_200_one_409_and_at_most_one_tracked_action()
    {
        Guid dana = await UserIdAsync(SeededApi.SomeoneWhoSignsIn.DisplayName);
        Guid marcus = await UserIdAsync("Marcus Bell");

        // Several proposals, so a serialized interleaving (the loser loads a decided proposal and
        // Decide refuses) and a true race (both load it Pending and the commit refuses) are both
        // likely to be exercised. Each must answer the same way. Odd attempts race a Reject
        // against the Approve: there only the proposal's xmin guards, because a Reject writes no
        // Tracked Action for the unique index to refuse.
        for (int attempt = 0; attempt < 8; attempt++)
        {
            bool rejectRaces = attempt % 2 == 1;

            Guid proposal = await SavedProposalAsync(SeededApi.SomeoneWhoSignsIn.DisplayName);

            using HttpClient first = ClientAs(dana);
            using HttpClient second = ClientAs(marcus);

            HttpResponseMessage[] responses = await Task.WhenAll(
                PostAsync(first, proposal, Approve(Description, dana, "2026-10-01")),
                PostAsync(second, proposal, rejectRaces ? new { decision = "Reject" } : Approve("Book movers", null, "2026-10-02")));

            HttpStatusCode[] statuses = [.. responses.Select(response => response.StatusCode).Order()];

            Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], statuses);

            await AssertProblemAsync(
                responses.Single(response => response.StatusCode == HttpStatusCode.Conflict),
                HttpStatusCode.Conflict,
                ProblemTypes.Conflict);

            // A winning Approve leaves exactly one Tracked Action; a winning Reject leaves none.
            bool rejectWon = rejectRaces && responses[1].StatusCode == HttpStatusCode.OK;

            Assert.Equal(rejectWon ? 0 : 1, await TrackedActionCountAsync(proposal));
        }
    }

    // ---------------------------------------------------------------------------------------
    // The contract
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task The_contract_publishes_the_decision_and_no_way_to_post_a_tracked_action()
    {
        await using TestApi api = new();
        using HttpClient _ = api.CreateClient();

        JsonElement paths = JsonDocument
            .Parse(await OpenApiExport.GenerateAsync(api.Services, TestContext.Current.CancellationToken))
            .RootElement
            .GetProperty("paths");

        Assert.True(paths.GetProperty("/api/v1/proposed-actions/{id}/decision").TryGetProperty("post", out JsonElement _));

        // AD-3 — only a decision creates a Tracked Action.
        Assert.DoesNotContain(
            paths.EnumerateObject(),
            path => path.Name.StartsWith("/api/v1/tracked-actions", StringComparison.Ordinal)
                && path.Value.TryGetProperty("post", out JsonElement _));
    }

    // ---------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------

    private static object Approve(string? description, Guid? ownerUserId, string? dueDate) =>
        new { decision = "Approve", description, ownerUserId, dueDate };

    private HttpClient ClientAs(Guid userId)
    {
        HttpClient client = seeded.Api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestApi.TokenFor(nameof(Role.ActionOfficer), subject: userId.ToString()));

        return client;
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, Guid proposal, object body) =>
        client.PostAsJsonAsync(DecisionPath(proposal), body, TestContext.Current.CancellationToken);

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement.Clone();

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string type)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        JsonElement problem = await ReadAsync(response);

        Assert.Equal(type, problem.GetProperty("type").GetString());
        Assert.Equal((int)status, problem.GetProperty("status").GetInt32());
    }

    /// <summary>A fresh Meeting, run and one Pending proposal, committed through the repositories.</summary>
    private async Task<Guid> SavedProposalAsync(string suggestedOwner)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        Meeting meeting = Meeting.Create($"Decision test {Guid.CreateVersion7():N}", Held, ["Dana Whitfield"], Guid.CreateVersion7(), Created);
        meeting.AttachNotes("Dana will order the scanners.", Created);

        ExtractionRun run = ExtractionRun.Start(
            meeting.Id, meeting.Notes!.Id, meeting.Notes.Sha256, Guid.CreateVersion7(),
            Metrics, ExtractionOutcome.Succeeded, null, warnings: null);

        IReadOnlyList<ActionRevision> revisions = run.AddProposals(
            [new ProposedActionDraft(Description, suggestedOwner, Proposed, 0.91, "Dana will order the scanners.")],
            Created);

        await using AsyncServiceScope scope = seeded.Api.Services.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<IMeetingRepository>().Add(meeting);
        scope.ServiceProvider.GetRequiredService<IExtractionRunRepository>().Add(run);
        scope.ServiceProvider.GetRequiredService<IActionRevisionRepository>().AddRange(revisions);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);

        return run.Proposals[0].Id;
    }

    private async Task<T> WithContextAsync<T>(Func<AppDbContext, Task<T>> read)
    {
        await using AsyncServiceScope scope = seeded.Api.Services.CreateAsyncScope();

        return await read(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private Task<Guid> UserIdAsync(string displayName) =>
        WithContextAsync(context => context.Users
            .Where(user => user.DisplayName == displayName)
            .Select(user => user.Id)
            .SingleAsync(TestContext.Current.CancellationToken));

    private Task<Guid> SystemUserIdAsync() =>
        WithContextAsync(context => context.Users
            .Where(user => user.IsSystem)
            .Select(user => user.Id)
            .SingleAsync(TestContext.Current.CancellationToken));

    private Task<ProposedAction> ProposalAsync(Guid proposal) =>
        WithContextAsync(context => context.Set<ProposedAction>()
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == proposal, TestContext.Current.CancellationToken));

    private Task<TrackedAction> TrackedActionAsync(Guid proposal) =>
        WithContextAsync(context => context.TrackedActions
            .AsNoTracking()
            .SingleAsync(action => action.ProposedActionId == proposal, TestContext.Current.CancellationToken));

    private Task<int> TrackedActionCountAsync(Guid proposal) =>
        WithContextAsync(context => context.TrackedActions
            .CountAsync(action => action.ProposedActionId == proposal, TestContext.Current.CancellationToken));

    /// <summary>The revisions a decision wrote: those after the AiProposal, over both targets.</summary>
    private Task<int> RevisionCountAsync(Guid proposal, Guid trackedAction) =>
        WithContextAsync(context => context.ActionRevisions
            .CountAsync(
                revision => revision.Kind != RevisionKind.AiProposal
                    && (revision.TargetId == proposal || revision.TargetId == trackedAction),
                TestContext.Current.CancellationToken));
}
