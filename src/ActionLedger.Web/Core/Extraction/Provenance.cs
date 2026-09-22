namespace ActionLedger.Web.Core.Extraction;

/// <summary>
/// DESIGN.md, Provenance chip — who a value came from. It sits in <c>Core</c> for the reason
/// <see cref="ReviewState"/> does: <c>ProvenanceChip</c> in <c>Shared/</c> renders it, and the
/// Review Screen, Action Detail and the Audit Trail all hand it one.
/// </summary>
public enum Provenance
{
    /// <summary>The model produced it: "Proposed by AI".</summary>
    Ai,

    /// <summary>A person decided it: "Decided by {display name}".</summary>
    Human,
}
