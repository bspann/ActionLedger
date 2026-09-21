namespace ActionLedger.Web.Core.Auth;

/// <summary>
/// AD-14 — one of only two cross-feature state services, and the one the PRD's FR-23 describes:
/// the token is held in memory by a scoped service. Nothing here reaches browser storage of any
/// kind, and no cookie is written, so a browser reload signs the user out by decision.
/// </summary>
/// <remarks>
/// Scoped is effectively singleton in a WebAssembly host, so the delegating handler, the layout,
/// and every page share one instance and <see cref="Changed"/> is the single signal the shell
/// re-renders on.
/// </remarks>
public sealed class SessionState
{
    private string? attemptedRoute;

    /// <summary>Raised after every sign-in and sign-out. The shell subscribes to it.</summary>
    public event Action? Changed;

    public bool IsSignedIn => Token is not null;

    public string? Token { get; private set; }

    public DateTimeOffset? ExpiresAt { get; private set; }

    public Guid? UserId { get; private set; }

    public string? DisplayName { get; private set; }

    /// <summary>
    /// The role name, as a string. AD-14 again: the generated <c>Role</c> enum may not be seen
    /// above the seam, and the toolbar renders this value directly.
    /// </summary>
    public string? Role { get; private set; }

    public void SignIn(SignedInUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        Token = user.Token;
        ExpiresAt = user.ExpiresAt;
        UserId = user.UserId;
        DisplayName = user.DisplayName;
        Role = user.Role;

        Changed?.Invoke();
    }

    /// <summary>
    /// Clears every session field, including any captured route. The delegating handler captures
    /// the attempted route <em>after</em> calling this, which is why clearing it here is safe and
    /// why a deliberate sign-out cannot leave a stale route behind to hijack the next login.
    /// </summary>
    public void SignOut()
    {
        Token = null;
        ExpiresAt = null;
        UserId = null;
        DisplayName = null;
        Role = null;
        attemptedRoute = null;

        Changed?.Invoke();
    }

    /// <summary>
    /// Remembers where the user was heading when the session ended, so sign-in can return them
    /// there. This is the mechanism a route guard would use; Story 1.6 ships no guard because it
    /// ships no protected page, but the handler and the login page exercise this end to end.
    /// </summary>
    public void CaptureAttemptedRoute(string route) => attemptedRoute = route;

    /// <summary>
    /// Returns the captured route and forgets it. Restore-once is the whole point: a route left
    /// lying around would send a later, unrelated sign-in somewhere the user never asked to go.
    /// </summary>
    public string? TakeAttemptedRoute()
    {
        string? route = attemptedRoute;
        attemptedRoute = null;

        return route;
    }
}
