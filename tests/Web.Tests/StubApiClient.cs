using ActionLedger.Web.Core.Api;

namespace ActionLedger.Web.Tests;

/// <summary>
/// A hand-written stand-in for the generated client. The repository takes no mocking package
/// (NFR8, NFR9), and <c>/GenerateClientInterfaces:true</c> on the generator is what makes writing
/// one possible at all.
/// </summary>
/// <remarks>
/// Every operation records its call count and its arguments, so "called exactly once" and "called
/// nothing else" are both assertable. The health operations throw: nothing in the web app calls
/// them, and a service that quietly grew one fails here rather than passing quietly.
/// </remarks>
internal sealed class StubApiClient : IActionLedgerApiClient
{
    internal SignInCommand? LastSignInCommand { get; private set; }

    internal CancellationToken LastSignInCancellationToken { get; private set; }

    internal int SignInCalls { get; private set; }

    /// <summary>Thrown from <c>SignInAsync</c> instead of returning, when set.</summary>
    internal Exception? SignInThrows { get; set; }

    /// <summary>Awaited by <c>SignInAsync</c> before it answers, so a test can hold a call open.</summary>
    internal Task? SignInGate { get; set; }

    internal SignInResult SignInResult { get; set; } = new()
    {
        Token = "stub-token",
        ExpiresAt = DateTimeOffset.UnixEpoch,
        User = new UserSummaryDto { Id = Guid.Empty, DisplayName = "Ada", Role = Role.Lead },
    };

    internal int ListUsersCalls { get; private set; }

    internal int? LastPage { get; private set; }

    internal int? LastPageSize { get; private set; }

    /// <summary>Thrown from <c>ListUsersAsync</c> instead of returning, when set.</summary>
    internal Exception? ListUsersThrows { get; set; }

    internal PagedResultOfUserSummaryDto Roster { get; set; } = new()
    {
        Items = [],
        Page = 1,
        PageSize = 200,
        Total = 0,
    };

    public Task<SignInResult> SignInAsync(SignInCommand body) => SignInAsync(body, CancellationToken.None);

    public async Task<SignInResult> SignInAsync(SignInCommand body, CancellationToken cancellationToken)
    {
        SignInCalls++;
        LastSignInCommand = body;
        LastSignInCancellationToken = cancellationToken;

        if (SignInGate is not null)
        {
            await SignInGate;
        }

        return SignInThrows is null ? SignInResult : throw SignInThrows;
    }

    public Task<PagedResultOfUserSummaryDto> ListUsersAsync(int? page, int? pageSize) =>
        ListUsersAsync(page, pageSize, CancellationToken.None);

    public Task<PagedResultOfUserSummaryDto> ListUsersAsync(int? page, int? pageSize, CancellationToken cancellationToken)
    {
        ListUsersCalls++;
        LastPage = page;
        LastPageSize = pageSize;

        return ListUsersThrows is null ? Task.FromResult(Roster) : Task.FromException<PagedResultOfUserSummaryDto>(ListUsersThrows);
    }

    public Task<HealthStatus> GetHealthAsync() => throw NotExercised();

    public Task<HealthStatus> GetHealthAsync(CancellationToken cancellationToken) => throw NotExercised();

    public Task<HealthStatus> GetReadinessAsync() => throw NotExercised();

    public Task<HealthStatus> GetReadinessAsync(CancellationToken cancellationToken) => throw NotExercised();

    /// <summary>Every decision sent, in order, with the proposal it was sent for.</summary>
    internal List<(Guid Id, DecideProposalCommand Command)> Decisions { get; } = [];

    /// <summary>Thrown from <c>DecideProposedActionAsync</c> instead of returning, when set.</summary>
    internal Exception? DecideThrows { get; set; }

    /// <summary>Awaited by <c>DecideProposedActionAsync</c> before it answers, so a test can hold a decision in flight.</summary>
    internal Task? DecideGate { get; set; }

    /// <summary>
    /// Run after the gate and before the answer, so a test can make the server's state follow the
    /// decision — typically by replacing <see cref="Run"/> with the decided run the refresh reads.
    /// Not run when <see cref="DecideThrows"/> is set.
    /// </summary>
    internal Action<Guid, DecideProposalCommand>? OnDecide { get; set; }

    internal ReviewState DecidedState { get; set; } = ReviewState.Approved;

    public Task<ProposalDecisionDto> DecideProposedActionAsync(Guid id, DecideProposalCommand body) =>
        DecideProposedActionAsync(id, body, CancellationToken.None);

    public async Task<ProposalDecisionDto> DecideProposedActionAsync(Guid id, DecideProposalCommand body, CancellationToken cancellationToken)
    {
        Decisions.Add((id, body));

        if (DecideGate is not null)
        {
            await DecideGate;
        }

        if (DecideThrows is not null)
        {
            throw DecideThrows;
        }

        OnDecide?.Invoke(id, body);

        return new ProposalDecisionDto
        {
            ProposedActionId = id,
            ReviewState = DecidedState,
            TrackedActionId = DecidedState is ReviewState.Rejected ? null : Guid.CreateVersion7(),
            DecidedByUserId = Guid.Empty,
            DecidedAt = DateTimeOffset.UnixEpoch,
        };
    }

    internal int StartExtractionRunCalls { get; private set; }

    internal Guid LastStartExtractionRunId { get; private set; }

    /// <summary>Thrown from <c>StartExtractionRunAsync</c> instead of returning, when set.</summary>
    internal Exception? StartExtractionRunThrows { get; set; }

    /// <summary>
    /// Awaited by <c>StartExtractionRunAsync</c> before it answers, so a test can hold a run in
    /// flight and assert what the page renders — and leaves interactive — meanwhile.
    /// </summary>
    internal Task? StartExtractionRunGate { get; set; }

    internal RunDto StartedRun { get; set; } = new()
    {
        Id = Guid.Empty,
        Outcome = ExtractionOutcome.Succeeded,
    };

    internal int GetExtractionRunCalls { get; private set; }

    internal Guid LastGetExtractionRunId { get; private set; }

    /// <summary>Thrown from <c>GetExtractionRunAsync</c> instead of returning, when set.</summary>
    internal Exception? GetExtractionRunThrows { get; set; }

    internal RunDetailDto Run { get; set; } = new()
    {
        Id = Guid.Empty,
        MeetingId = Guid.Empty,
        MeetingNotesId = Guid.Empty,
        NotesSha256 = new string('a', 64),
        StartedByUserId = Guid.Empty,
        Provider = "Fake",
        Model = "fixture-catalog",
        PromptVersion = "v1",
        SchemaVersion = "1",
        StartedAt = new DateTimeOffset(2026, 9, 22, 9, 15, 0, TimeSpan.Zero),
        DurationMs = 87,
        InputTokens = 0,
        OutputTokens = 0,
        Outcome = ExtractionOutcome.Succeeded,
        FailureReason = null,
        Warnings = [],
        Proposals = [],
    };

    internal int ListExtractionRunsCalls { get; private set; }

    internal Guid LastListExtractionRunsId { get; private set; }

    /// <summary>Thrown from <c>ListExtractionRunsAsync</c> instead of returning, when set.</summary>
    internal Exception? ListExtractionRunsThrows { get; set; }

    /// <summary>Empty by default: a meeting nobody has run yet.</summary>
    internal List<RunSummaryDto> Runs { get; set; } = [];

    internal int GetAiProviderCalls { get; private set; }

    /// <summary>Thrown from <c>GetAiProviderAsync</c> instead of returning, when set.</summary>
    internal Exception? GetAiProviderThrows { get; set; }

    /// <summary>What the real host reports under the Fake provider.</summary>
    internal AiProviderDto AiProvider { get; set; } = new() { Provider = "Fake", Model = "fixture-catalog" };

    public Task<RunDto> StartExtractionRunAsync(Guid id) => StartExtractionRunAsync(id, CancellationToken.None);

    public async Task<RunDto> StartExtractionRunAsync(Guid id, CancellationToken cancellationToken)
    {
        StartExtractionRunCalls++;
        LastStartExtractionRunId = id;

        if (StartExtractionRunGate is not null)
        {
            await StartExtractionRunGate;
        }

        return StartExtractionRunThrows is null ? StartedRun : throw StartExtractionRunThrows;
    }

    public Task<RunDetailDto> GetExtractionRunAsync(Guid id) => GetExtractionRunAsync(id, CancellationToken.None);

    public Task<RunDetailDto> GetExtractionRunAsync(Guid id, CancellationToken cancellationToken)
    {
        GetExtractionRunCalls++;
        LastGetExtractionRunId = id;

        return GetExtractionRunThrows is null ? Task.FromResult(Run) : Task.FromException<RunDetailDto>(GetExtractionRunThrows);
    }

    public Task<ICollection<RunSummaryDto>> ListExtractionRunsAsync(Guid id) =>
        ListExtractionRunsAsync(id, CancellationToken.None);

    public Task<ICollection<RunSummaryDto>> ListExtractionRunsAsync(Guid id, CancellationToken cancellationToken)
    {
        ListExtractionRunsCalls++;
        LastListExtractionRunsId = id;

        // A copy, so a test that changes Runs after a read is not rewriting what the page holds.
        return ListExtractionRunsThrows is null
            ? Task.FromResult<ICollection<RunSummaryDto>>([.. Runs])
            : Task.FromException<ICollection<RunSummaryDto>>(ListExtractionRunsThrows);
    }

    public Task<AiProviderDto> GetAiProviderAsync() => GetAiProviderAsync(CancellationToken.None);

    public Task<AiProviderDto> GetAiProviderAsync(CancellationToken cancellationToken)
    {
        GetAiProviderCalls++;

        return GetAiProviderThrows is null ? Task.FromResult(AiProvider) : Task.FromException<AiProviderDto>(GetAiProviderThrows);
    }

    internal int CreateMeetingCalls { get; private set; }

    internal CreateMeetingCommand? LastCreateMeetingCommand { get; private set; }

    /// <summary>Thrown from <c>CreateMeetingAsync</c> instead of returning, when set.</summary>
    internal Exception? CreateMeetingThrows { get; set; }

    /// <summary>
    /// Awaited by <c>CreateMeetingAsync</c> before it answers, so a test can hold the call open
    /// and assert that the dialog's Create button cannot fire a second time.
    /// </summary>
    internal Task? CreateMeetingGate { get; set; }

    internal MeetingCreatedDto MeetingCreated { get; set; } = new()
    {
        Id = Guid.Empty,
        CreatedByUserId = Guid.Empty,
    };

    internal int ListMeetingsCalls { get; private set; }

    internal int? LastMeetingsPage { get; private set; }

    internal int? LastMeetingsPageSize { get; private set; }

    /// <summary>Thrown from <c>ListMeetingsAsync</c> instead of returning, when set.</summary>
    internal Exception? ListMeetingsThrows { get; set; }

    /// <summary>
    /// Awaited by <c>ListMeetingsAsync</c> before it answers, so a test can hold a load open.
    /// Unlike the other gates it is awaited <em>with</em> the call's token, because the caller
    /// this one exists for is <c>MudTable</c>, which cancels the outstanding request before
    /// starting the next one. A gate that ignored the token could not produce that cancellation.
    /// </summary>
    internal Task? ListMeetingsGate { get; set; }

    internal PagedResultOfMeetingSummaryDto MeetingPage { get; set; } = new()
    {
        Items = [],
        Page = 1,
        PageSize = 50,
        Total = 0,
    };

    internal int GetMeetingCalls { get; private set; }

    internal Guid LastGetMeetingId { get; private set; }

    /// <summary>Thrown from <c>GetMeetingAsync</c> instead of returning, when set.</summary>
    internal Exception? GetMeetingThrows { get; set; }

    /// <summary>
    /// Awaited by <c>GetMeetingAsync</c> before it answers, so a test can hold the load open and
    /// observe what Meeting Detail renders while the request is still in flight — which is the
    /// state <c>FocusOnNavigate</c> looks at.
    /// </summary>
    internal Task? GetMeetingGate { get; set; }

    internal MeetingDetailDto Meeting { get; set; } = new()
    {
        Id = Guid.Empty,
        Title = "Office move follow-up",
        MeetingDate = new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero),
        Attendees = [],
        CreatedByUserId = Guid.Empty,
        CreatedAt = DateTimeOffset.UnixEpoch,
        Notes = null,
    };

    internal int SaveMeetingNotesCalls { get; private set; }

    internal Guid LastSaveMeetingNotesId { get; private set; }

    internal SaveMeetingNotesCommand? LastSaveMeetingNotesCommand { get; private set; }

    /// <summary>Thrown from <c>SaveMeetingNotesAsync</c> instead of returning, when set.</summary>
    internal Exception? SaveMeetingNotesThrows { get; set; }

    /// <summary>Awaited by <c>SaveMeetingNotesAsync</c> before it answers, so a test can hold it open.</summary>
    internal Task? SaveMeetingNotesGate { get; set; }

    internal MeetingNotesDto SavedNotes { get; set; } = new()
    {
        Id = Guid.Empty,
        Text = "the notes",
        Sha256 = new string('a', 64),
        SavedAt = new DateTimeOffset(2026, 9, 21, 14, 3, 0, TimeSpan.Zero),
    };

    public Task<MeetingCreatedDto> CreateMeetingAsync(CreateMeetingCommand body) =>
        CreateMeetingAsync(body, CancellationToken.None);

    public async Task<MeetingCreatedDto> CreateMeetingAsync(CreateMeetingCommand body, CancellationToken cancellationToken)
    {
        CreateMeetingCalls++;
        LastCreateMeetingCommand = body;

        if (CreateMeetingGate is not null)
        {
            await CreateMeetingGate;
        }

        return CreateMeetingThrows is null ? MeetingCreated : throw CreateMeetingThrows;
    }

    public Task<PagedResultOfMeetingSummaryDto> ListMeetingsAsync(int? page, int? pageSize) =>
        ListMeetingsAsync(page, pageSize, CancellationToken.None);

    public async Task<PagedResultOfMeetingSummaryDto> ListMeetingsAsync(int? page, int? pageSize, CancellationToken cancellationToken)
    {
        ListMeetingsCalls++;
        LastMeetingsPage = page;
        LastMeetingsPageSize = pageSize;

        if (ListMeetingsGate is not null)
        {
            // WaitAsync is what makes the token observable: it abandons the wait with a
            // TaskCanceledException the moment the token fires, which is the shape the real
            // client produces when a request is cancelled mid-flight.
            await ListMeetingsGate.WaitAsync(cancellationToken);
        }

        return ListMeetingsThrows is null ? MeetingPage : throw ListMeetingsThrows;
    }

    public Task<MeetingNotesDto> SaveMeetingNotesAsync(Guid id, SaveMeetingNotesCommand body) =>
        SaveMeetingNotesAsync(id, body, CancellationToken.None);

    public async Task<MeetingNotesDto> SaveMeetingNotesAsync(Guid id, SaveMeetingNotesCommand body, CancellationToken cancellationToken)
    {
        SaveMeetingNotesCalls++;
        LastSaveMeetingNotesId = id;
        LastSaveMeetingNotesCommand = body;

        if (SaveMeetingNotesGate is not null)
        {
            await SaveMeetingNotesGate;
        }

        return SaveMeetingNotesThrows is null ? SavedNotes : throw SaveMeetingNotesThrows;
    }

    public Task<MeetingDetailDto> GetMeetingAsync(Guid id) => GetMeetingAsync(id, CancellationToken.None);

    public async Task<MeetingDetailDto> GetMeetingAsync(Guid id, CancellationToken cancellationToken)
    {
        GetMeetingCalls++;
        LastGetMeetingId = id;

        if (GetMeetingGate is not null)
        {
            await GetMeetingGate;
        }

        return GetMeetingThrows is null ? Meeting : throw GetMeetingThrows;
    }

    /// <summary>The generated <c>ApiException</c> shape for a status the server answers with a problem body.</summary>
    internal static ApiException<ProblemDetails> Problem(int statusCode, string? title, string? detail = null) =>
        new(
            "stub",
            statusCode,
            null,
            NoHeaders,
            new ProblemDetails { Status = statusCode, Title = title, Detail = detail },
            null);

    /// <summary>The generated shape for a 400. <c>ValidationProblemDetails</c> is a flat class, not a subclass.</summary>
    internal static ApiException<ValidationProblemDetails> Invalid(string? title) =>
        new(
            "stub",
            400,
            null,
            NoHeaders,
            new ValidationProblemDetails { Status = 400, Title = title },
            null);

    /// <summary>A bare <c>ApiException</c> — what a 500 raises, with no <c>type</c> and no <c>title</c>.</summary>
    internal static ApiException Bare(int statusCode) => new("stub", statusCode, null, NoHeaders, null);

    /// <summary>
    /// A bare <c>ApiException</c> carrying an undeserialised body. This is what an operation's
    /// undeclared status produces: the server's problem document, as text, in <c>Response</c>.
    /// </summary>
    internal static ApiException BareWithBody(int statusCode, string response) =>
        new("stub", statusCode, response, NoHeaders, null);

    private static readonly IReadOnlyDictionary<string, IEnumerable<string>> NoHeaders =
        new Dictionary<string, IEnumerable<string>>(StringComparer.Ordinal);

    private static NotSupportedException NotExercised() =>
        new("The web app calls neither health operation; a caller that grew one should fail here.");

}
