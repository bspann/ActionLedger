namespace ActionLedger.Domain.Users;

/// <summary>
/// What a User is allowed to do. Reads are open to any authenticated User; writes need either
/// role; the Cancelled transition needs <see cref="Lead"/> (AD-12).
/// </summary>
/// <remarks>Stored as a string and serialized as a PascalCase string (Consistency Conventions, Enums row).</remarks>
public enum Role
{
    /// <summary>Creates Meetings, runs extraction, and decides proposals.</summary>
    ActionOfficer,

    /// <summary>Everything an Action Officer may do, plus the Cancelled transition.</summary>
    Lead,
}
