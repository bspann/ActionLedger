using ActionLedger.Web.Core;
using ActionLedger.Web.Core.Api;
using ActionLedger.Web.Core.Auth;
using ActionLedger.Web.Core.Users;
using ActionLedger.Web.Features.Auth.Data;
using Xunit;

namespace ActionLedger.Web.Tests;

/// <summary>
/// AD-14 and AD-18 — a per-feature data service is the only place its feature reaches the API,
/// it is testable with the generated client stubbed, and it maps on the way out so no generated
/// type escapes the seam. The stub is hand-written because the repository takes no mocking
/// package (NFR8, NFR9).
/// </summary>
public sealed class AuthServiceTests
{
    [Fact]
    public async Task Sign_in_passes_the_credentials_through_to_the_generated_client()
    {
        StubApiClient client = new();
        AuthService service = new(client);

        await service.SignInAsync("ada", "correct horse", TestContext.Current.CancellationToken);

        Assert.NotNull(client.LastSignInCommand);
        Assert.Equal("ada", client.LastSignInCommand.Username);
        Assert.Equal("correct horse", client.LastSignInCommand.Password);
    }

    [Fact]
    public async Task Sign_in_maps_the_result_onto_web_owned_session_fields()
    {
        StubApiClient client = new();
        client.SignInResult.Token = "jwt";
        client.SignInResult.ExpiresAt = new DateTimeOffset(2026, 9, 21, 22, 3, 0, TimeSpan.Zero);
        client.SignInResult.User.Id = Guid.Parse("01999999-0000-7000-8000-00000000abcd");
        client.SignInResult.User.DisplayName = "Marcus Bell";

        AuthService service = new(client);

        SignInOutcome outcome = await service.SignInAsync("marcus", "pw", TestContext.Current.CancellationToken);

        Assert.Null(outcome.Failure);
        Assert.NotNull(outcome.User);
        Assert.Equal("jwt", outcome.User.Token);
        Assert.Equal(new DateTimeOffset(2026, 9, 21, 22, 3, 0, TimeSpan.Zero), outcome.User.ExpiresAt);
        Assert.Equal(Guid.Parse("01999999-0000-7000-8000-00000000abcd"), outcome.User.UserId);
        Assert.Equal("Marcus Bell", outcome.User.DisplayName);

        // The generated Role enum may not cross the seam, so the outcome carries its name.
        Assert.Equal(RoleNames.Lead, outcome.User.Role);
    }

    [Theory]
    [InlineData(Role.Lead, "Lead")]
    [InlineData(Role.ActionOfficer, "Action Officer")]
    public async Task Sign_in_maps_the_role_to_the_glossarys_wording(Role role, string expected)
    {
        // The enum spells it ActionOfficer because that is the wire format. Nobody reading the
        // toolbar should see it that way, and every other test here signs in a Lead, where the
        // two spellings happen to agree.
        StubApiClient client = new();
        client.SignInResult.User.Role = role;

        AuthService service = new(client);

        SignInOutcome outcome = await service.SignInAsync("ada", "pw", TestContext.Current.CancellationToken);

        Assert.Equal(expected, outcome.User?.Role);
    }

    [Fact]
    public async Task A_timed_out_login_comes_back_as_a_failure_rather_than_an_exception()
    {
        // A timeout throws TaskCanceledException, which is neither an ApiException nor an
        // HttpRequestException. Uncaught it escapes as an unhandled component exception instead
        // of the load-failure notice with Retry that the matrix requires.
        StubApiClient client = new() { SignInThrows = new TaskCanceledException("the request timed out") };
        AuthService service = new(client);

        SignInOutcome outcome = await service.SignInAsync("ada", "pw", TestContext.Current.CancellationToken);

        Assert.Null(outcome.User);
        Assert.NotNull(outcome.Failure);
        Assert.Equal(Voice.UnexpectedFailureTitle, outcome.Failure.Title);
    }

    [Fact]
    public async Task A_cancellation_the_caller_asked_for_still_propagates()
    {
        // Rendering "Couldn't load." at someone who navigated away would be wrong. The catch is
        // for failures, not for the caller's own decision to stop.
        StubApiClient client = new() { SignInThrows = new TaskCanceledException() };
        AuthService service = new(client);

        using CancellationTokenSource source = new();
        await source.CancelAsync();

        await Assert.ThrowsAsync<TaskCanceledException>(
            () => service.SignInAsync("ada", "pw", source.Token));
    }

    [Fact]
    public async Task Sign_in_hands_its_cancellation_token_to_the_generated_client()
    {
        StubApiClient client = new();
        AuthService service = new(client);

        using CancellationTokenSource source = new();

        await service.SignInAsync("ada", "correct horse", source.Token);

        Assert.Equal(source.Token, client.LastSignInCancellationToken);
    }

    [Fact]
    public async Task A_refusal_comes_back_as_a_failure_rather_than_an_exception()
    {
        // Without this catch, LoginPage would have to catch ApiException itself — and it cannot,
        // because ApiException is a Core.Api type and Features.Auth is outside the seam.
        StubApiClient client = new() { SignInThrows = StubApiClient.Problem(401, "Authentication is required.") };
        AuthService service = new(client);

        SignInOutcome outcome = await service.SignInAsync("ada", "wrong", TestContext.Current.CancellationToken);

        Assert.Null(outcome.User);
        Assert.NotNull(outcome.Failure);
        Assert.Equal(401, outcome.Failure.StatusCode);
        Assert.Equal("Authentication is required.", outcome.Failure.Title);
    }

    [Fact]
    public async Task A_transport_failure_comes_back_as_a_failure_rather_than_an_exception()
    {
        // HttpRequestException is not an ApiException, so it needs its own catch in the service.
        StubApiClient client = new() { SignInThrows = new HttpRequestException("no route to host") };
        AuthService service = new(client);

        SignInOutcome outcome = await service.SignInAsync("ada", "pw", TestContext.Current.CancellationToken);

        Assert.Null(outcome.User);
        Assert.NotNull(outcome.Failure);
        Assert.Equal(Voice.UnexpectedFailureTitle, outcome.Failure.Title);
    }

    [Fact]
    public async Task Sign_in_calls_the_login_operation_and_nothing_else()
    {
        StubApiClient client = new();
        AuthService service = new(client);

        await service.SignInAsync("ada", "correct horse", TestContext.Current.CancellationToken);

        Assert.Equal(1, client.SignInCalls);
        Assert.Equal(0, client.ListUsersCalls);
    }
}
