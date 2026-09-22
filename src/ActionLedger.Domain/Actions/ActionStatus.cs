namespace ActionLedger.Domain.Actions;

/// <summary>
/// Where a <see cref="TrackedAction"/> stands in its lifecycle. The four statuses are fixed by
/// <c>prd.md:76</c>.
/// </summary>
/// <remarks>
/// <para>
/// Only <see cref="Open"/> is reachable in Story 3.1: a Tracked Action is created Open by
/// <c>ProposedAction.Decide</c>, and nothing changes it until Epic 4's
/// <c>TrackedAction.Transition</c>. The whole enum is declared now, following
/// <c>ReviewState</c>'s precedent, so the published contract never has to move when it does.
/// </para>
/// <para>Stored as a string and serialized as a PascalCase string (Consistency Conventions, Enums row).</para>
/// </remarks>
public enum ActionStatus
{
    /// <summary>Approved and not yet started. Every Tracked Action starts here.</summary>
    Open,

    /// <summary>Someone is working on it. Written by Epic 4.</summary>
    InProgress,

    /// <summary>Done. Written by Epic 4.</summary>
    Complete,

    /// <summary>No longer going to happen. Written by Epic 4.</summary>
    Cancelled,
}
