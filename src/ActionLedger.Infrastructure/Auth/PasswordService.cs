using ActionLedger.Domain.Users;
using Microsoft.AspNetCore.Identity;

namespace ActionLedger.Infrastructure.Auth;

/// <summary>
/// AD-12 — passwords go through <see cref="PasswordHasher{TUser}"/>. Hashing and verification
/// live in this ring and nowhere else; Domain holds a hash and never looks at it.
/// </summary>
/// <remarks>
/// <c>PasswordHasher&lt;User&gt;</c> defaults to PBKDF2-HMAC-SHA512 with a per-password salt, and
/// <see cref="Verify"/> reports a rehash need so Story 1.4 can upgrade a stored hash on a
/// successful sign-in without changing this class.
/// </remarks>
public sealed class PasswordService
{
    /// <summary>
    /// <see cref="PasswordHasher{TUser}"/> takes a user only to satisfy its generic signature —
    /// the shipped implementation never reads it, but it does reject null. A password has to be
    /// hashed before the User that will hold it can be constructed, so hashing goes through this
    /// one instance, which is never persisted and never leaves this class.
    /// </summary>
    private static readonly User HashingSubject =
        User.RegisterSystem("hashing-subject", "Hashing Subject", "not-a-hash", DateTimeOffset.UnixEpoch);

    private readonly PasswordHasher<User> _hasher = new();

    /// <summary>Hashes a password for storage.</summary>
    public string Hash(string password) => _hasher.HashPassword(HashingSubject, password);

    /// <summary>Checks a password against the User's stored hash.</summary>
    /// <returns>
    /// <see cref="PasswordVerificationResult.Success"/>,
    /// <see cref="PasswordVerificationResult.SuccessRehashNeeded"/> when the stored hash uses
    /// older parameters, or <see cref="PasswordVerificationResult.Failed"/>.
    /// </returns>
    public PasswordVerificationResult Verify(User user, string password)
    {
        try
        {
            return _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        }
        catch (FormatException)
        {
            // A stored value that is not a hash at all — a row someone edited by hand. Not a
            // crash, and not a sign-in either.
            return PasswordVerificationResult.Failed;
        }
    }
}
