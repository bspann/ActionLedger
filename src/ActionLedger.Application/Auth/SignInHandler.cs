using System.ComponentModel.DataAnnotations;
using ActionLedger.Application.Abstractions;
using ActionLedger.Application.Users;
using ActionLedger.Domain.Users;

namespace ActionLedger.Application.Auth;

/// <summary>
/// AD-2 — the first <c>&lt;Verb&gt;&lt;Noun&gt;Handler</c>, and the shape every later write use
/// case copies: one class, one public <c>HandleAsync</c>, ports for everything it cannot do itself.
/// </summary>
/// <remarks>
/// <para>
/// There is exactly one failure value — <c>null</c> — for all three ways a sign-in can fail, so
/// the controller has nothing to leak with. An unknown username, a wrong password, and the system
/// identity are indistinguishable to the caller by construction rather than by discipline.
/// </para>
/// <para>
/// No rehash-on-success. <c>PasswordCheck</c> has no rehash value because <c>User</c> has no
/// mutator to write a new hash and every stored hash came from one hasher configuration; see the
/// story's Design Notes. Sign-in is a read, and it commits nothing.
/// </para>
/// </remarks>
public sealed class SignInHandler(
    IUserRepository users,
    IPasswordVerifier passwords,
    IAccessTokenIssuer tokens)
{
    /// <summary>Signs a User in, or returns <c>null</c> if anything at all was wrong.</summary>
    public async Task<SignInResult?> HandleAsync(
        SignInCommand command,
        CancellationToken cancellationToken = default)
    {
        // Matched as stored — the repository lower-cases and trims, exactly as User.Register did
        // when the row was written. "Dana" and " dana " are the same sign-in name.
        User? user = await users.FindByUsernameAsync(command.Username, cancellationToken);

        if (user is null)
        {
            return null;
        }

        // Verify before the system check, so an existing User costs the same work whichever way
        // it ends. The system identity's stored hash is of a value nobody holds, so this already
        // fails for it — the check below is the guard that holds even if that ever changed.
        if (passwords.Verify(user, command.Password) is not PasswordCheck.Succeeded)
        {
            return null;
        }

        if (user.IsSystem)
        {
            // AD-21's Seed User owns seeded rows. It is an author, not an account.
            return null;
        }

        AccessToken token = tokens.Issue(user);

        return new SignInResult(token.Token, token.ExpiresAt, UserSummaryDto.From(user));
    }
}

/// <summary>
/// The sign-in request. It is also the body <c>POST /api/v1/auth/login</c> binds, so the
/// annotations here are what turn a blank field into <c>[ApiController]</c>'s 400 rather than
/// into a 401 that would imply the credentials were merely wrong.
/// </summary>
public sealed record SignInCommand
{
    /// <summary>
    /// The longest password the route accepts. Generous enough that no real passphrase is ever
    /// refused, and small enough that an anonymous caller cannot hand PBKDF2 a multi-megabyte
    /// body for a username the demo publishes. Rejected by model validation, before any hashing.
    /// </summary>
    public const int PasswordMaxLength = 256;

    /// <summary>The sign-in name, as typed. Casing and surrounding space do not matter.</summary>
    [Required(ErrorMessage = "A username is required.")]
    [StringLength(User.UsernameMaxLength, ErrorMessage = "A username cannot exceed 64 characters.")]
    public string Username { get; init; } = string.Empty;

    /// <summary>The password, as typed. Never logged and never echoed back.</summary>
    [Required(ErrorMessage = "A password is required.")]
    [StringLength(PasswordMaxLength, ErrorMessage = "A password cannot exceed 256 characters.")]
    public string Password { get; init; } = string.Empty;
}

/// <summary>A successful sign-in: the token, when it expires, and who the caller now is.</summary>
/// <param name="Token">The bearer token, sent as <c>Authorization: Bearer {token}</c>.</param>
/// <param name="ExpiresAt">When the token expires, in UTC.</param>
/// <param name="User">The signed-in User, in the one shape that crosses the boundary.</param>
public sealed record SignInResult(string Token, DateTimeOffset ExpiresAt, UserSummaryDto User);
