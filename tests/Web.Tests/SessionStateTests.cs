using ActionLedger.Web.Core.Auth;
using Xunit;

namespace ActionLedger.Web.Tests;

/// <summary>
/// AD-14 and FR-23 — the token lives in memory in one scoped service, and the attempted route it
/// captures is restored exactly once. Restore-once is what stops a stale route hijacking a later,
/// unrelated sign-in.
/// </summary>
public sealed class SessionStateTests
{
    private static readonly SignedInUser Marcus = new(
        "jwt",
        new DateTimeOffset(2026, 9, 22, 6, 3, 0, TimeSpan.Zero),
        Guid.Parse("01999999-0000-7000-8000-00000000abcd"),
        "Marcus Bell",
        "Lead");

    [Fact]
    public void A_fresh_session_holds_nothing()
    {
        SessionState session = new();

        Assert.False(session.IsSignedIn);
        Assert.Null(session.Token);
        Assert.Null(session.ExpiresAt);
        Assert.Null(session.UserId);
        Assert.Null(session.DisplayName);
        Assert.Null(session.Role);
    }

    [Fact]
    public void Signing_in_holds_every_field_the_shell_renders()
    {
        SessionState session = new();

        session.SignIn(Marcus);

        Assert.True(session.IsSignedIn);
        Assert.Equal("jwt", session.Token);
        Assert.Equal(new DateTimeOffset(2026, 9, 22, 6, 3, 0, TimeSpan.Zero), session.ExpiresAt);
        Assert.Equal(Guid.Parse("01999999-0000-7000-8000-00000000abcd"), session.UserId);
        Assert.Equal("Marcus Bell", session.DisplayName);
        Assert.Equal("Lead", session.Role);
    }

    [Fact]
    public void Signing_out_clears_every_field()
    {
        SessionState session = new();
        session.SignIn(Marcus);

        session.SignOut();

        Assert.False(session.IsSignedIn);
        Assert.Null(session.Token);
        Assert.Null(session.ExpiresAt);
        Assert.Null(session.UserId);
        Assert.Null(session.DisplayName);
        Assert.Null(session.Role);
    }

    [Fact]
    public void Signing_in_and_out_both_raise_changed()
    {
        // The shell re-renders on this signal and on nothing else, so a silent change would
        // leave the toolbar showing the previous session.
        SessionState session = new();
        int raised = 0;
        session.Changed += () => raised++;

        session.SignIn(Marcus);
        session.SignOut();

        Assert.Equal(2, raised);
    }

    [Fact]
    public void The_captured_route_comes_back_once_and_then_not_at_all()
    {
        SessionState session = new();

        session.CaptureAttemptedRoute("/actions/7");

        Assert.Equal("/actions/7", session.TakeAttemptedRoute());
        Assert.Null(session.TakeAttemptedRoute());
    }

    [Fact]
    public void There_is_no_captured_route_until_one_is_captured()
    {
        SessionState session = new();

        Assert.Null(session.TakeAttemptedRoute());
    }

    [Fact]
    public void Signing_out_forgets_the_captured_route()
    {
        // The handler captures after it signs out, so this ordering is deliberate: a deliberate
        // sign-out from the user menu cannot leave a route behind to redirect the next person.
        SessionState session = new();
        session.SignIn(Marcus);
        session.CaptureAttemptedRoute("/actions/7");

        session.SignOut();

        Assert.Null(session.TakeAttemptedRoute());
    }
}
