namespace ActionLedger.Domain.Extraction;

/// <summary>
/// Where a <see cref="ProposedAction"/> stands in review. The four states are fixed by
/// <c>prd.md:69</c> and none of them is ever removed.
/// </summary>
/// <remarks>
/// <para>
/// Only <see cref="Pending"/> is reachable in Story 2.5: a proposal is created Pending and nothing
/// in the extraction path changes it. Story 3.2's <c>ProposedAction.Decide</c> writes the other
/// three, and it is the only thing that ever will (AD-3). The whole enum is declared now so the
/// published contract — and the web client generated from it — never has to move when it does.
/// </para>
/// <para>Stored as a string and serialized as a PascalCase string (Consistency Conventions, Enums row).</para>
/// </remarks>
public enum ReviewState
{
    /// <summary>Nobody has decided yet. Every proposal starts here, and in this story stays here.</summary>
    Pending,

    /// <summary>Accepted as the AI proposed it. Written by Story 3.2.</summary>
    Approved,

    /// <summary>Accepted with changes to the description, owner, or due date. Written by Story 3.2.</summary>
    Edited,

    /// <summary>Refused, with a reason. Written by Story 3.2.</summary>
    Rejected,
}
