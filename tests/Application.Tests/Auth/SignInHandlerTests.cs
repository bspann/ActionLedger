using ActionLedger.Application.Abstractions;
using ActionLedger.Application.Auth;
using ActionLedger.Domain.Users;
using Xunit;

namespace ActionLedger.Application.Tests.Auth;

/// <summary>
/// AD-12 — sign-in issues an 8-hour token for a real human User, and refuses everything else with
/// one indistinguishable answer. AD-18 — the ports are in-memory fakes here; the Api suite proves
/// the same rows against a real database and the real bearer scheme.
/// </summary>
public sealed class SignInHandlerTests
{
    private static readonly DateTimeOffset Registered = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The credentials this run invented. NFR5 keeps credential values out of the repository, and
    /// a test constant is still one — so the accepted password is generated per test instance and
    /// handed to the fake verifier, and nothing here can pass by agreeing with a value on disk.
    /// </summary>
    private readonly string _rightPassword = NewPassword();

    private readonly string _wrongPassword = NewPassword();

    [Fact]
    public async Task A_valid_credential_returns_a_token_and_the_callers_own_summary()
    {
        User dana = Human("dana", "Dana Whitfield", Role.ActionOfficer);
        SignInHandler handler = HandlerFor(dana);

        SignInResult? result = await handler.HandleAsync(
            Credential("dana", _rightPassword),
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(FakeTokenIssuer.TokenFor(dana), result.Token);
        Assert.Equal(FakeTokenIssuer.Expiry, result.ExpiresAt);

        // The summary is the caller's own, not a stand-in built from what they typed.
        Assert.Equal(dana.Id, result.User.Id);
        Assert.Equal("Dana Whitfield", result.User.DisplayName);
        Assert.Equal(Role.ActionOfficer, result.User.Role);
    }

    [Fact]
    public async Task A_wrong_password_is_refused()
    {
        SignInHandler handler = HandlerFor(Human("dana", "Dana Whitfield", Role.ActionOfficer));

        Assert.Null(await handler.HandleAsync(
            Credential("dana", _wrongPassword),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_unknown_username_is_refused()
    {
        SignInHandler handler = HandlerFor(Human("dana", "Dana Whitfield", Role.ActionOfficer));

        Assert.Null(await handler.HandleAsync(
            Credential("nobody", _rightPassword),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_system_identity_is_never_signed_in_even_with_the_right_password()
    {
        // The seeded system User's stored hash is of a value nobody holds, so in production this
        // never gets past verification. The fake verifier says yes on purpose: the guard being
        // tested is the IsSystem refusal itself, not the hash that happens to shadow it.
        User seed = User.RegisterSystem("seed", "Seed", "hash-of-seed", Registered);
        SignInHandler handler = HandlerFor(seed);

        Assert.Null(await handler.HandleAsync(
            Credential("seed", _rightPassword),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Every_refusal_is_the_same_answer_so_the_controller_has_nothing_to_leak()
    {
        User dana = Human("dana", "Dana Whitfield", Role.ActionOfficer);
        User seed = User.RegisterSystem("seed", "Seed", "hash-of-seed", Registered);
        SignInHandler handler = HandlerFor(dana, seed);

        SignInResult?[] refusals =
        [
            await handler.HandleAsync(Credential("dana", _wrongPassword), TestContext.Current.CancellationToken),
            await handler.HandleAsync(Credential("nobody", _rightPassword), TestContext.Current.CancellationToken),
            await handler.HandleAsync(Credential("seed", _rightPassword), TestContext.Current.CancellationToken),
        ];

        // There is one failure value, so "wrong password", "no such user", and "that is not an
        // account" cannot be told apart by anything downstream. This is structural, not a habit.
        Assert.All(refusals, Assert.Null);
    }

    [Theory]
    [InlineData("Dana")]
    [InlineData(" dana ")]
    [InlineData("DANA")]
    public async Task A_username_is_matched_as_stored_so_casing_and_padding_do_not_matter(string typed)
    {
        SignInHandler handler = HandlerFor(Human("dana", "Dana Whitfield", Role.ActionOfficer));

        Assert.NotNull(await handler.HandleAsync(
            Credential(typed, _rightPassword),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_refused_sign_in_never_asks_for_a_token()
    {
        FakeTokenIssuer tokens = new();
        SignInHandler handler = new(
            new FakeUserRepository(Human("dana", "Dana Whitfield", Role.ActionOfficer)),
            new FakePasswordVerifier(_rightPassword),
            tokens);

        await handler.HandleAsync(Credential("dana", _wrongPassword), TestContext.Current.CancellationToken);

        Assert.Equal(0, tokens.Issued);
    }

    private static User Human(string username, string displayName, Role role) =>
        User.Register(username, displayName, $"hash-of-{username}", role, Registered);

    private static SignInCommand Credential(string username, string password) =>
        new() { Username = username, Password = password };

    private static string NewPassword() => $"test-{Guid.CreateVersion7():N}";

    private SignInHandler HandlerFor(params User[] users) =>
        new(new FakeUserRepository(users), new FakePasswordVerifier(_rightPassword), new FakeTokenIssuer());

    /// <summary>Matches usernames the way the real repository does: trimmed and lower-cased.</summary>
    private sealed class FakeUserRepository(params User[] users) : IUserRepository
    {
        private readonly List<User> _users = [.. users];

        public void Add(User user) => _users.Add(user);

        public Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_users.FirstOrDefault(user => user.Id == id));

        public Task<User?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default)
        {
            string normalized = (username ?? string.Empty).Trim().ToLowerInvariant();

            return Task.FromResult(_users.FirstOrDefault(user =>
                string.Equals(user.Username, normalized, StringComparison.Ordinal)));
        }
    }

    /// <summary>Accepts the one password it was given and nothing else, whichever User is asking.</summary>
    private sealed class FakePasswordVerifier(string accepted) : IPasswordVerifier
    {
        public PasswordCheck Verify(User user, string password) =>
            string.Equals(password, accepted, StringComparison.Ordinal)
                ? PasswordCheck.Succeeded
                : PasswordCheck.Failed;
    }

    /// <summary>Issues a token derived from the User, and counts how often it was asked.</summary>
    private sealed class FakeTokenIssuer : IAccessTokenIssuer
    {
        public static readonly DateTimeOffset Expiry = new(2026, 9, 21, 8, 0, 0, TimeSpan.Zero);

        public int Issued { get; private set; }

        public static string TokenFor(User user) => $"token-for-{user.Id}";

        public AccessToken Issue(User user)
        {
            Issued++;

            return new AccessToken(TokenFor(user), Expiry);
        }
    }
}
