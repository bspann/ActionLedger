using ActionLedger.Web.Core.Api;

namespace ActionLedger.Web.Core.Users;

/// <summary>
/// The PRD Glossary's names for the two Roles, which EXPERIENCE.md says are used verbatim. The
/// generated <c>Role</c> enum spells <c>ActionOfficer</c> as one word because that is what the
/// wire format carries; nobody reading the screen should see it that way.
/// </summary>
/// <remarks>
/// One place, in <c>Core</c>, because both sides of the seam produce this string: <c>AuthService</c>
/// maps the signed-in user's Role and <c>UserDirectory</c> maps the roster's, and the toolbar and
/// Epic 3's owner picker render whatever they were handed without being able to check it.
/// </remarks>
public static class RoleNames
{
    /// <summary>An Action Officer creates and works Tracked Actions.</summary>
    public const string ActionOfficer = "Action Officer";

    /// <summary>A Lead may additionally cancel one.</summary>
    public const string Lead = "Lead";

    /// <summary>
    /// The display name for a Role. An unrecognised value — a role added to the contract but not
    /// yet here — falls back to the wire name rather than rendering nothing.
    /// </summary>
    public static string Display(Role role) => role switch
    {
        Role.ActionOfficer => ActionOfficer,
        Role.Lead => Lead,
        _ => role.ToString(),
    };
}
