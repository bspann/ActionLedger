using ActionLedger.Application.Abstractions;
using ActionLedger.Domain.Users;
using Microsoft.AspNetCore.Identity;

namespace ActionLedger.Infrastructure.Auth;

/// <summary>
/// AD-1 Rule 3 — the one place <see cref="PasswordVerificationResult"/> is read. It is a
/// <c>Microsoft.AspNetCore.Identity</c> type, so it stops in this ring and Application sees
/// <see cref="PasswordCheck"/> instead.
/// </summary>
internal sealed class PasswordVerifier(PasswordService passwords) : IPasswordVerifier
{
    public PasswordCheck Verify(User user, string password) => passwords.Verify(user, password) switch
    {
        // SuccessRehashNeeded is a successful verification. Rehashing on sign-in is deliberately
        // not done: `User` has no mutator to write a new hash, sign-in commits nothing, and every
        // stored hash came from the one hasher configuration in this repository — so the branch
        // cannot currently occur. See the story's Design Notes.
        PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded =>
            PasswordCheck.Succeeded,
        _ => PasswordCheck.Failed,
    };
}
