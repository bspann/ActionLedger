namespace ActionLedger.Domain.Actions;

/// <summary>
/// AD-7 — what an <see cref="ActionRevision"/> is about. It is a discriminator rather than a
/// navigation: the revision root has no navigation from any aggregate, so
/// <c>(TargetType, TargetId)</c> is how a row names what it audits.
/// </summary>
/// <remarks>
/// <para>
/// Only <see cref="ProposedAction"/> is written in Story 2.5. <c>TrackedAction</c> arrives with
/// Story 3.2, which is the story that first writes a revision against one.
/// </para>
/// <para>Stored as a string and serialized as a PascalCase string (Consistency Conventions, Enums row).</para>
/// </remarks>
public enum RevisionTargetType
{
    /// <summary>A <c>ProposedAction</c> row. The only target this story produces.</summary>
    ProposedAction,
}
