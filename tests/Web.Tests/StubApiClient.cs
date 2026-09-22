using ActionLedger.Web.Core.Api;

namespace ActionLedger.Web.Tests;

/// <summary>
/// A hand-written stand-in for the generated client. The repository takes no mocking package
/// (NFR8, NFR9), and <c>/GenerateClientInterfaces:true</c> on the generator is what makes writing
/// one possible at all.
/// </summary>
/// <remarks>
/// Every operation records its call count and its arguments, so "called exactly once" and "called
/// nothing else" are both assertable. The two health operations throw: nothing in the web app
/// calls them, and a service that quietly grew one fails here rather than passing quietly. The
/// four Meeting operations throw for the same reason — Story 2.1 publishes them in the contract
/// and Story 2.2 is what gives the web app a screen that calls them.
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

    public Task<MeetingCreatedDto> CreateMeetingAsync(CreateMeetingCommand body) => throw NoMeetingScreenYet();

    public Task<MeetingCreatedDto> CreateMeetingAsync(CreateMeetingCommand body, CancellationToken cancellationToken) =>
        throw NoMeetingScreenYet();

    public Task<PagedResultOfMeetingSummaryDto> ListMeetingsAsync(int? page, int? pageSize) => throw NoMeetingScreenYet();

    public Task<PagedResultOfMeetingSummaryDto> ListMeetingsAsync(int? page, int? pageSize, CancellationToken cancellationToken) =>
        throw NoMeetingScreenYet();

    public Task<MeetingNotesDto> SaveMeetingNotesAsync(Guid id, SaveMeetingNotesCommand body) => throw NoMeetingScreenYet();

    public Task<MeetingNotesDto> SaveMeetingNotesAsync(Guid id, SaveMeetingNotesCommand body, CancellationToken cancellationToken) =>
        throw NoMeetingScreenYet();

    public Task<MeetingDetailDto> GetMeetingAsync(Guid id) => throw NoMeetingScreenYet();

    public Task<MeetingDetailDto> GetMeetingAsync(Guid id, CancellationToken cancellationToken) => throw NoMeetingScreenYet();

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

    private static NotSupportedException NoMeetingScreenYet() =>
        new("Story 2.1 publishes the Meeting operations; Story 2.2 is what makes the web app call them.");
}
