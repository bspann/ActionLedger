using ActionLedger.Web.Core.Errors;

namespace ActionLedger.Web.Core.Auth;

/// <summary>
/// AD-14 — the session fields a successful sign-in produces, in web-owned types. <c>Role</c> is a
/// string rather than the generated <c>Role</c> enum so the toolbar can render it without
/// crossing the seam.
/// </summary>
public sealed record SignedInUser(
    string Token,
    DateTimeOffset ExpiresAt,
    Guid UserId,
    string DisplayName,
    string Role);

/// <summary>
/// AD-14 — what <c>AuthService</c> returns. Without it the login page could not call the service
/// at all: the generated <c>SignInResult</c> and <c>ApiException</c> are
/// <c>ActionLedger.Web.Core.Api</c> types, and <c>Features.Auth</c> is outside the seam allowlist
/// <c>WebStructureTests</c> enforces.
/// </summary>
/// <remarks>
/// It lives in <c>Core/Auth</c> rather than beside the service because <see cref="SessionState"/>
/// consumes <see cref="SignedInUser"/> too.
/// </remarks>
public sealed record SignInOutcome
{
    private SignInOutcome(SignedInUser? user, ApiFailure? failure)
    {
        User = user;
        Failure = failure;
    }

    /// <summary>The session fields, or <c>null</c> when the sign-in did not succeed.</summary>
    public SignedInUser? User { get; }

    /// <summary>The failure, or <c>null</c> when the sign-in succeeded.</summary>
    public ApiFailure? Failure { get; }

    public static SignInOutcome Succeeded(SignedInUser user) => new(user, null);

    public static SignInOutcome Failed(ApiFailure failure) => new(null, failure);
}
