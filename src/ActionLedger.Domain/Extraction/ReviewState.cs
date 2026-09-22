namespace ActionLedger.Domain.Extraction;

/// <summary>
/// Where a <see cref="ProposedAction"/> stands in review. The four states are fixed by
/// <c>prd.md:69</c> and none of them is ever removed.
/// </summary>
/// <remarks>
/// <para>
/// A proposal is created Pending and nothing in the extraction path changes it. Story 3.1's
/// <c>ProposedAction.Decide</c> writes the other three, once, and it is the only thing that ever
/// will (AD-3): a decided proposal is never decided again. The whole enum was declared with
/// Story 2.5 so the published contract — and the web client generated from it — never had to move.
/// </para>
/// <para>Stored as a string and serialized as a PascalCase string (Consistency Conventions, Enums row).</para>
/// </remarks>
public enum ReviewState
{
    /// <summary>Nobody has decided yet. Every proposal starts here, and only a Pending one can be decided.</summary>
    Pending,

    /// <summary>Accepted as the AI proposed it. Written by <c>ProposedAction.Decide</c>.</summary>
    Approved,

    /// <summary>Accepted with changes to the description, owner, or due date. Written by <c>ProposedAction.Decide</c>.</summary>
    Edited,

    /// <summary>Refused, with an optional reason. Written by <c>ProposedAction.Decide</c>.</summary>
    Rejected,
}
