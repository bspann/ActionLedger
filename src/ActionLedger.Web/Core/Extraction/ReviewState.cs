namespace ActionLedger.Web.Core.Extraction;

/// <summary>
/// AD-14 — where a Proposed Action stands, in a web-owned type. It sits in <c>Core</c> rather than
/// in a feature because <c>ReviewStateChip</c> in <c>Shared/</c> renders it, and Run Detail today
/// and the Epic 3 Review Screen later both hand it one.
/// </summary>
/// <remarks>The member names are the Review State names, and the chip renders them as its text.</remarks>
public enum ReviewState
{
    /// <summary>Not decided yet. Every proposal is Pending until Epic 3 decides one.</summary>
    Pending,

    /// <summary>Approved as proposed.</summary>
    Approved,

    /// <summary>Approved with at least one field changed.</summary>
    Edited,

    /// <summary>Rejected, with or without a reason.</summary>
    Rejected,
}
