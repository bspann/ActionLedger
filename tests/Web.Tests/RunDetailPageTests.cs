using ActionLedger.Web.Core;
using ActionLedger.Web.Core.Api;
using ActionLedger.Web.Core.Auth;
using ActionLedger.Web.Core.Shell;
using ActionLedger.Web.Core.Users;
using ActionLedger.Web.Features.Review;
using ActionLedger.Web.Features.Review.Data;
using ActionLedger.Web.Shared;
using AngleSharp.Dom;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Xunit;
using ApiReviewState = ActionLedger.Web.Core.Api.ReviewState;

namespace ActionLedger.Web.Tests;

/// <summary>
/// FR-6 and FR-9 — Run Detail. The metadata is a definition list carrying every FR-6 field with
/// tokens as "0", warnings expand to the dropped excerpts verbatim, a Failed run shows its reason
/// and "Run again", and the proposals render in AI order with a Review State chip and the three
/// decision cells present and empty.
/// </summary>
public sealed class RunDetailPageTests : BunitContext
{
    private static readonly Guid MeetingId = Guid.Parse("01999999-0000-7000-8000-00000000beef");

    private static readonly Guid RunId = Guid.Parse("01999999-0000-7000-8000-0000000000aa");

    private static readonly Guid NewRunId = Guid.Parse("01999999-0000-7000-8000-0000000000bb");

    private const string Reason = "The AI returned invalid output twice.";

    private const string Warning = "Dropped: excerpt 'Marcus will resurface the lot.' was not found in the notes.";

    private readonly StubApiClient client = new();
    private readonly SessionState session = new();

    public RunDetailPageTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;

        Services.AddSingleton<IActionLedgerApiClient>(client);
        Services.AddSingleton(session);
        Services.AddSingleton(new LoadingState());
        Services.AddSingleton<UserDirectory>();
        Services.AddSingleton<ReviewService>();

        client.Run = Run();
        client.StartedRun = new RunDto { Id = NewRunId, Outcome = ExtractionOutcome.Succeeded };
    }

    [Fact]
    public void The_metadata_is_a_definition_list_not_a_table()
    {
        SignIn();

        IRenderedComponent<RunDetailPage> page = RenderDetail();

        IElement metadata = page.Find($"#{RunDetailPage.MetadataId}");

        // DESIGN.md: "Plain two-column definition list, not a table."
        Assert.Equal("DL", metadata.TagName);
        Assert.Empty(metadata.QuerySelectorAll("table"));
        Assert.Equal(metadata.QuerySelectorAll("dt").Length, metadata.QuerySelectorAll(":scope > dd").Length);
    }

    [Fact]
    public void Every_FR6_field_renders_with_tokens_as_zero()
    {
        SignIn();

        IRenderedComponent<RunDetailPage> page = RenderDetail();

        Assert.Equal(Voice.ExtractionRun, page.Find("h1").TextContent);

        string[] terms = [.. page.FindAll($"#{RunDetailPage.MetadataId} dt").Select(term => term.TextContent)];

        Assert.Equal(
            [
                Voice.AiProvider, Voice.Model, Voice.PromptVersion, Voice.SchemaVersion, Voice.Started,
                Voice.Duration, Voice.InputTokens, Voice.OutputTokens, Voice.Outcome, Voice.Warnings,
            ],
            terms);

        Assert.Equal("Fake", Text(page, RunDetailPage.ProviderId));
        Assert.Equal("fixture-catalog", Text(page, RunDetailPage.ModelId));
        Assert.Equal("v1", Text(page, RunDetailPage.PromptVersionId));
        Assert.Equal("1", Text(page, RunDetailPage.SchemaVersionId));
        Assert.Equal("2026-09-22 09:15 UTC", Text(page, RunDetailPage.StartedId));
        Assert.Equal("87 ms", Text(page, RunDetailPage.DurationId));

        // FR-6 — the Fake's zero is rendered, never a blank.
        Assert.Equal("0", Text(page, RunDetailPage.InputTokensId));
        Assert.Equal("0", Text(page, RunDetailPage.OutputTokensId));

        Assert.Equal(Voice.Succeeded, Text(page, RunDetailPage.OutcomeId));

        // Only a Failed run has a reason term and a Run again button.
        Assert.Empty(page.FindAll($"#{RunDetailPage.FailureReasonId}"));
        Assert.Empty(page.FindAll($"#{RunDetailPage.RunAgainId}"));
    }

    [Fact]
    public void Large_counts_render_with_group_separators()
    {
        // The repo's other counts group their thousands (Formats.Count); a local model's run
        // reads "41,250 ms", not "41250 ms".
        SignIn();

        RunDetailDto run = Run();
        run.DurationMs = 41_250;
        run.InputTokens = 1_812;
        run.OutputTokens = 406;
        client.Run = run;

        IRenderedComponent<RunDetailPage> page = RenderDetail();

        Assert.Equal("41,250 ms", Text(page, RunDetailPage.DurationId));
        Assert.Equal("1,812", Text(page, RunDetailPage.InputTokensId));
        Assert.Equal("406", Text(page, RunDetailPage.OutputTokensId));
    }

    [Fact]
    public void The_warnings_count_expands_to_each_warning_verbatim()
    {
        SignIn();
        client.Run = Run(warnings: [Warning]);

        IRenderedComponent<RunDetailPage> page = RenderDetail();

        Assert.Equal("1", Text(page, RunDetailPage.WarningsCountId));

        page.FindAll(".mud-expand-panel-header")
            .Single(header => header.TextContent.Contains(Voice.DroppedExcerpts, StringComparison.Ordinal))
            .Click();

        page.WaitForAssertion(() => Assert.Equal(
            [Warning],
            page.FindAll(".al-warning").Select(line => line.TextContent)));
    }

    [Fact]
    public void A_clean_run_shows_a_warnings_count_of_zero_and_nothing_to_expand()
    {
        SignIn();

        IRenderedComponent<RunDetailPage> page = RenderDetail();

        Assert.Equal("0", Text(page, RunDetailPage.WarningsCountId));
        Assert.Empty(page.FindAll(".mud-expand-panel"));
    }

    [Fact]
    public void Proposals_render_in_the_order_received_with_chip_confidence_and_empty_decision_cells()
    {
        SignIn();
        client.Run = Run(proposals:
        [
            Proposal(0, "Order the replacement scanners.", 0.91, isLowConfidence: false),
            Proposal(1, "Book the range.", 0.62, isLowConfidence: true),
        ]);

        IRenderedComponent<RunDetailPage> page = RenderDetail();

        IElement[] rows = Rows(page);

        Assert.Equal(2, rows.Length);
        Assert.Equal(
            ["Order the replacement scanners.", "Book the range."],
            rows.Select(row => row.QuerySelector(".al-proposal-description")!.TextContent));

        // Two decimals, and the Low Confidence badge only where the server flagged it.
        Assert.Equal(["0.91", "0.62"], rows.Select(row => row.QuerySelector(".al-confidence-score")!.TextContent));
        Assert.Null(rows[0].QuerySelector(".al-low-confidence"));

        IElement badge = rows[1].QuerySelector(".al-low-confidence")!;
        Assert.Equal(Voice.LowConfidence, badge.TextContent.Trim());
        Assert.NotNull(badge.QuerySelector("svg"));

        Assert.All(rows, row =>
        {
            Assert.Equal(Voice.Pending, row.QuerySelector(".mud-chip")!.TextContent.Trim());
            Assert.Contains(ReviewStateChip.ModifierFor(Core.Extraction.ReviewState.Pending), row.QuerySelector(".mud-chip")!.ClassList);

            // Visible cells, empty until Story 3.1 publishes decisions.
            Assert.Equal(string.Empty, row.QuerySelector(".al-decided-by")!.TextContent.Trim());
            Assert.Equal(string.Empty, row.QuerySelector(".al-decided-at")!.TextContent.Trim());
            Assert.Equal(string.Empty, row.QuerySelector(".al-rejection-reason")!.TextContent.Trim());
        });

        string[] headers = [.. page.FindAll($"#{RunDetailPage.ProposalsId} th").Select(header => header.TextContent.Trim())];

        Assert.Equal(
            [Voice.Description, Voice.Confidence, Voice.ReviewState, Voice.DecidedBy, Voice.Decided, Voice.RejectionReason],
            headers);
    }

    [Fact]
    public void A_run_that_kept_nothing_says_so()
    {
        SignIn();

        IRenderedComponent<RunDetailPage> page = RenderDetail();

        Assert.Equal(Voice.NoProposals, Text(page, RunDetailPage.NoProposalsId));
    }

    [Fact]
    public void A_failed_run_shows_its_reason_verbatim_and_run_again()
    {
        SignIn();
        client.Run = Run(outcome: ExtractionOutcome.Failed, failureReason: Reason);

        IRenderedComponent<RunDetailPage> page = RenderDetail();

        Assert.Equal(Voice.Failed, Text(page, RunDetailPage.OutcomeId));
        Assert.Equal(Voice.ExtractionFailedPrefix + Reason, Text(page, RunDetailPage.FailureReasonId));
        Assert.Equal(Voice.RunAgain, page.Find($"#{RunDetailPage.RunAgainId}").TextContent.Trim());
    }

    [Theory]
    [InlineData(ExtractionOutcome.Succeeded)]
    [InlineData(ExtractionOutcome.Failed)]
    public async Task Run_again_posts_for_the_runs_meeting_and_opens_the_new_run_whatever_its_outcome(ExtractionOutcome outcome)
    {
        SignIn();
        client.Run = Run(outcome: ExtractionOutcome.Failed, failureReason: Reason);
        client.StartedRun = new RunDto { Id = NewRunId, Outcome = outcome };

        IRenderedComponent<RunDetailPage> page = RenderDetail();

        await page.Find($"#{RunDetailPage.RunAgainId}").ClickAsync(new MouseEventArgs());

        Assert.Equal(1, client.StartExtractionRunCalls);
        Assert.Equal(MeetingId, client.LastStartExtractionRunId);
        Assert.EndsWith($"/meetings/{MeetingId}/runs/{NewRunId}", Navigation.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_again_cannot_fire_twice_while_its_call_is_outstanding()
    {
        TaskCompletionSource gate = new();
        client.StartExtractionRunGate = gate.Task;

        SignIn();
        client.Run = Run(outcome: ExtractionOutcome.Failed, failureReason: Reason);

        IRenderedComponent<RunDetailPage> page = RenderDetail();

        Task running = page.Find($"#{RunDetailPage.RunAgainId}").ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => Assert.True(page.Find($"#{RunDetailPage.RunAgainId}").HasAttribute("disabled")));
        page.Find($"#{RunDetailPage.RunAgainId}").Click();

        Assert.Equal(1, client.StartExtractionRunCalls);

        gate.SetResult();
        await running;
    }

    [Fact]
    public async Task A_failed_run_again_raises_a_snackbar_with_a_retry_that_runs_again()
    {
        SignIn();
        client.Run = Run(outcome: ExtractionOutcome.Failed, failureReason: Reason);
        client.StartExtractionRunThrows = StubApiClient.Bare(500);

        IRenderedComponent<MudSnackbarProvider> snackbars = Render<MudSnackbarProvider>();
        IRenderedComponent<RunDetailPage> page = RenderDetail();

        await page.Find($"#{RunDetailPage.RunAgainId}").ClickAsync(new MouseEventArgs());

        snackbars.WaitForAssertion(() =>
            Assert.Contains(Voice.UnexpectedFailureTitle, snackbars.Markup, StringComparison.Ordinal));

        // Stayed put, and the button is handed back.
        Assert.False(page.Find($"#{RunDetailPage.RunAgainId}").HasAttribute("disabled"));
        Assert.DoesNotContain(NewRunId.ToString(), Navigation.Uri, StringComparison.Ordinal);

        client.StartExtractionRunThrows = null;

        await snackbars.FindAll("button")
            .First(button => string.Equals(button.TextContent.Trim(), Voice.Retry, StringComparison.Ordinal))
            .ClickAsync(new MouseEventArgs());

        Assert.Equal(2, client.StartExtractionRunCalls);
        page.WaitForAssertion(() =>
            Assert.EndsWith($"/meetings/{MeetingId}/runs/{NewRunId}", Navigation.Uri, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(409)]
    public async Task A_refusal_the_session_handler_already_announced_offers_no_retry(int status)
    {
        SignIn();
        client.Run = Run(outcome: ExtractionOutcome.Failed, failureReason: Reason);
        client.StartExtractionRunThrows = StubApiClient.Problem(status, "Refused.");

        IRenderedComponent<MudSnackbarProvider> snackbars = Render<MudSnackbarProvider>();
        IRenderedComponent<RunDetailPage> page = RenderDetail();

        await page.Find($"#{RunDetailPage.RunAgainId}").ClickAsync(new MouseEventArgs());

        Assert.Equal(1, client.StartExtractionRunCalls);
        Assert.DoesNotContain("Refused.", snackbars.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(
            snackbars.FindAll("button"),
            button => string.Equals(button.TextContent.Trim(), Voice.Retry, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_retry_pressed_after_the_user_has_left_starts_no_run()
    {
        SignIn();
        client.Run = Run(outcome: ExtractionOutcome.Failed, failureReason: Reason);
        client.StartExtractionRunThrows = StubApiClient.Bare(500);

        IRenderedComponent<MudSnackbarProvider> snackbars = Render<MudSnackbarProvider>();
        IRenderedComponent<RunDetailPage> page = RenderDetail();

        await page.Find($"#{RunDetailPage.RunAgainId}").ClickAsync(new MouseEventArgs());

        snackbars.WaitForAssertion(() =>
            Assert.Contains(Voice.UnexpectedFailureTitle, snackbars.Markup, StringComparison.Ordinal));

        // Only the page is torn down; the snackbar provider lives in the layout and stays.
        page.Instance.Dispose();
        client.StartExtractionRunThrows = null;

        await snackbars.FindAll("button")
            .First(button => string.Equals(button.TextContent.Trim(), Voice.Retry, StringComparison.Ordinal))
            .ClickAsync(new MouseEventArgs());

        Assert.Equal(1, client.StartExtractionRunCalls);
    }

    [Fact]
    public async Task A_run_again_that_lands_after_the_user_has_left_does_not_pull_them_back()
    {
        TaskCompletionSource gate = new();
        client.StartExtractionRunGate = gate.Task;

        SignIn();
        client.Run = Run(outcome: ExtractionOutcome.Failed, failureReason: Reason);

        IRenderedComponent<RunDetailPage> page = RenderDetail();

        Task running = page.Find($"#{RunDetailPage.RunAgainId}").ClickAsync(new MouseEventArgs());

        Navigation.NavigateTo("/meetings");
        await DisposeComponentsAsync();

        gate.SetResult();
        await running;

        Assert.EndsWith("/meetings", Navigation.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void Arriving_at_another_run_on_the_same_component_reloads_it()
    {
        // "Run again" navigates between two Run Detail URLs, and the router reuses the component,
        // so only the parameters change. Without the reload the old run stays on screen under the
        // new run's address.
        SignIn();
        client.Run = Run(outcome: ExtractionOutcome.Failed, failureReason: Reason);

        IRenderedComponent<RunDetailPage> page = RenderDetail();

        client.Run = Run(id: NewRunId);

        page.Render(parameters => parameters
            .Add(component => component.MeetingId, MeetingId)
            .Add(component => component.RunId, NewRunId));

        page.WaitForAssertion(() => Assert.Equal(Voice.Succeeded, Text(page, RunDetailPage.OutcomeId)));
        Assert.Equal(2, client.GetExtractionRunCalls);
        Assert.Equal(NewRunId, client.LastGetExtractionRunId);
    }

    [Fact]
    public void An_unknown_run_renders_the_not_found_notice_linking_to_the_meeting()
    {
        SignIn();
        client.GetExtractionRunThrows = StubApiClient.Problem(404, "The resource was not found.");

        IRenderedComponent<RunDetailPage> page = RenderDetail();

        Assert.Equal(Voice.NotFound, page.Find("h1").TextContent);
        Assert.Equal($"/meetings/{MeetingId}", page.Find("a").GetAttribute("href"));
        Assert.Empty(page.FindAll($"#{RunDetailPage.MetadataId}"));
    }

    [Fact]
    public void A_run_that_belongs_to_another_meeting_is_not_found_at_this_address()
    {
        SignIn();
        client.Run = Run(meetingId: Guid.CreateVersion7());

        IRenderedComponent<RunDetailPage> page = RenderDetail();

        Assert.Equal(Voice.NotFound, page.Find("h1").TextContent);
        Assert.Empty(page.FindAll($"#{RunDetailPage.MetadataId}"));
    }

    [Fact]
    public void A_failed_load_renders_the_notice_and_retry_re_reads_the_run()
    {
        SignIn();
        client.GetExtractionRunThrows = StubApiClient.Bare(500);

        IRenderedComponent<RunDetailPage> page = RenderDetail();

        Assert.Contains(Voice.LoadFailurePrefix + Voice.UnexpectedFailureTitle, page.Markup, StringComparison.Ordinal);

        // A heading in every state, for FocusOnNavigate.
        Assert.Equal(Voice.ExtractionRun, page.Find("h1").TextContent);

        client.GetExtractionRunThrows = null;
        page.Find($"#{LoadFailure.RetryId}").Click();

        page.WaitForAssertion(() => Assert.Equal("Fake", Text(page, RunDetailPage.ProviderId)));
        Assert.Equal(2, client.GetExtractionRunCalls);
    }

    [Fact]
    public void An_unauthenticated_visitor_is_sent_to_login_with_the_attempted_route_captured()
    {
        string route = $"/meetings/{MeetingId}/runs/{RunId}";
        Navigation.NavigateTo(route);

        RenderDetail();

        Assert.Equal("/login", Navigation.History.First().Uri);
        Assert.Equal(route, session.TakeAttemptedRoute());
        Assert.Equal(0, client.GetExtractionRunCalls);
    }

    [Fact]
    public void The_back_link_returns_to_the_meeting()
    {
        SignIn();

        IRenderedComponent<RunDetailPage> page = RenderDetail();

        IElement back = page.Find($"#{RunDetailPage.BackToMeetingId}");

        Assert.Equal(Voice.BackToMeeting, back.TextContent);
        Assert.Equal($"/meetings/{MeetingId}", back.GetAttribute("href"));
    }

    private BunitNavigationManager Navigation =>
        (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();

    private IRenderedComponent<RunDetailPage> RenderDetail() =>
        Render<RunDetailPage>(parameters => parameters
            .Add(component => component.MeetingId, MeetingId)
            .Add(component => component.RunId, RunId));

    private void SignIn() =>
        session.SignIn(new SignedInUser("jwt", DateTimeOffset.UnixEpoch, Guid.Empty, "Dana Whitfield", RoleNames.ActionOfficer));

    private static string Text(IRenderedComponent<RunDetailPage> page, string id) => page.Find($"#{id}").TextContent;

    private static IElement[] Rows(IRenderedComponent<RunDetailPage> page) =>
        [.. page.FindAll($"#{RunDetailPage.ProposalsId} tbody tr")];

    private static RunDetailDto Run(
        Guid? id = null,
        Guid? meetingId = null,
        ExtractionOutcome outcome = ExtractionOutcome.Succeeded,
        string? failureReason = null,
        string[]? warnings = null,
        ProposedActionDto[]? proposals = null) => new()
    {
        Id = id ?? RunId,
        MeetingId = meetingId ?? MeetingId,
        MeetingNotesId = Guid.Empty,
        NotesSha256 = new string('a', 64),
        StartedByUserId = Guid.Empty,
        Provider = "Fake",
        Model = "fixture-catalog",
        PromptVersion = "v1",
        SchemaVersion = "1",
        StartedAt = new DateTimeOffset(2026, 9, 22, 9, 15, 42, TimeSpan.Zero),
        DurationMs = 87,
        InputTokens = 0,
        OutputTokens = 0,
        Outcome = outcome,
        FailureReason = failureReason,
        Warnings = warnings ?? [],
        Proposals = proposals ?? [],
    };

    private static ProposedActionDto Proposal(int ordinal, string description, double confidence, bool isLowConfidence) => new()
    {
        Id = Guid.CreateVersion7(),
        Ordinal = ordinal,
        Description = description,
        SuggestedOwner = string.Empty,
        SuggestedDueDate = null,
        Confidence = confidence,
        SourceExcerpt = description,
        IsLowConfidence = isLowConfidence,
        SuggestedOwnerUserId = null,
        ReviewState = ApiReviewState.Pending,
    };
}
