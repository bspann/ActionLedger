namespace ActionLedger.Domain.Actions;

/// <summary>
/// AD-7 — what an <see cref="ActionRevision"/> is about. It is a discriminator rather than a
/// navigation: the revision root has no navigation from any aggregate, so
/// <c>(TargetType, TargetId)</c> is how a row names what it audits.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ProposedAction"/> rows are the AiProposal and ReviewDecision revisions.
/// <see cref="TrackedAction"/> rows are the FieldEdit revisions a decision writes against the
/// work it created, and later Epic 4's status changes and edits.
/// </para>
/// <para>Stored as a string and serialized as a PascalCase string (Consistency Conventions, Enums row).</para>
/// </remarks>
public enum RevisionTargetType
{
    /// <summary>A <c>ProposedAction</c> row.</summary>
    ProposedAction,

    /// <summary>A <c>TrackedAction</c> row.</summary>
    TrackedAction,
}
