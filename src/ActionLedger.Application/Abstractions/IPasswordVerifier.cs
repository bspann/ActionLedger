using ActionLedger.Domain.Users;

namespace ActionLedger.Application.Abstractions;

/// <summary>
/// AD-12 — checking a password against a stored hash, expressed in types this ring owns.
/// </summary>
/// <remarks>
/// The hasher's own result type is <c>Microsoft.AspNetCore.Identity.PasswordVerificationResult</c>,
/// and AD-1 Rule 3 bans that namespace here. <c>PasswordVerifier</c> in Infrastructure is the one
/// place the Identity enum is read, and it is where <see cref="PasswordCheck"/> is decided.
/// </remarks>
public interface IPasswordVerifier
{
    /// <summary>Checks a plaintext password against the User's stored hash.</summary>
    PasswordCheck Verify(User user, string password);
}

/// <summary>The outcome of checking a password. Two values, because a caller may do two things.</summary>
public enum PasswordCheck
{
    /// <summary>The password does not match the stored hash, or the stored value is not a hash.</summary>
    Failed,

    /// <summary>The password matches.</summary>
    Succeeded,
}
