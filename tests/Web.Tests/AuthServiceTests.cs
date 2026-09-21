using ActionLedger.Web.Core.Api;
using ActionLedger.Web.Features.Auth.Data;
using Xunit;

namespace ActionLedger.Web.Tests;

/// <summary>
/// AD-14 and AD-18 — a per-feature data service is the only place its feature reaches the API,
/// and it is testable with the generated client stubbed. The stub below is hand-written because
/// the repository takes no mocking package (NFR8, NFR9); <c>/GenerateClientInterfaces:true</c> on
/// the generator is what makes writing one possible at all.
/// </summary>
public sealed class AuthServiceTests
{
    [Fact]
    public async Task Sign_in_passes_the_credentials_through_to_the_generated_client()
    {
        StubApiClient client = new();
        AuthService service = new(client);

        await service.SignInAsync("ada", "correct horse", TestContext.Current.CancellationToken);

        Assert.NotNull(client.LastCommand);
        Assert.Equal("ada", client.LastCommand.Username);
        Assert.Equal("correct horse", client.LastCommand.Password);
    }

    [Fact]
    public async Task Sign_in_returns_the_result_the_client_produced()
    {
        StubApiClient client = new();
        AuthService service = new(client);

        SignInResult result = await service.SignInAsync("ada", "correct horse", TestContext.Current.CancellationToken);

        Assert.Same(client.Result, result);
    }

    [Fact]
    public async Task Sign_in_hands_its_cancellation_token_to_the_generated_client()
    {
        StubApiClient client = new();
        AuthService service = new(client);

        using CancellationTokenSource source = new();

        await service.SignInAsync("ada", "correct horse", source.Token);

        Assert.Equal(source.Token, client.LastCancellationToken);
    }

    /// <summary>
    /// A hand-written stand-in for the generated client. Only the login operation is exercised;
    /// the rest of the interface throws, so a service that quietly grew a second call fails here.
    /// </summary>
    private sealed class StubApiClient : IActionLedgerApiClient
    {
        internal SignInCommand? LastCommand { get; private set; }

        internal CancellationToken LastCancellationToken { get; private set; }

        internal SignInResult Result { get; } = new()
        {
            Token = "stub-token",
            ExpiresAt = DateTimeOffset.UnixEpoch,
            User = new UserSummaryDto { Id = Guid.Empty, DisplayName = "Ada", Role = Role.Lead },
        };

        public Task<SignInResult> SignInAsync(SignInCommand body) =>
            SignInAsync(body, CancellationToken.None);

        public Task<SignInResult> SignInAsync(SignInCommand body, CancellationToken cancellationToken)
        {
            LastCommand = body;
            LastCancellationToken = cancellationToken;

            return Task.FromResult(Result);
        }

        public Task<HealthStatus> GetHealthAsync() => throw NotExercised();

        public Task<HealthStatus> GetHealthAsync(CancellationToken cancellationToken) => throw NotExercised();

        public Task<HealthStatus> GetReadinessAsync() => throw NotExercised();

        public Task<HealthStatus> GetReadinessAsync(CancellationToken cancellationToken) => throw NotExercised();

        public Task<PagedResultOfUserSummaryDto> ListUsersAsync(int? page, int? pageSize) => throw NotExercised();

        public Task<PagedResultOfUserSummaryDto> ListUsersAsync(int? page, int? pageSize, CancellationToken cancellationToken) =>
            throw NotExercised();

        private static NotSupportedException NotExercised() =>
            new("AuthService is expected to call the login operation and nothing else.");
    }
}
