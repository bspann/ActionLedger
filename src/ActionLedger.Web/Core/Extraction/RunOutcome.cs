namespace ActionLedger.Web.Core.Extraction;

/// <summary>
/// AD-14 — how an Extraction Run ended, in a web-owned type. The generated
/// <c>ExtractionOutcome</c> is an <c>ActionLedger.Web.Core.Api</c> type, which pages may not see,
/// so the data services map onto this and nothing above them names the generated one.
/// </summary>
public enum RunOutcome
{
    /// <summary>The AI answered with output that validated. The run may still have kept nothing.</summary>
    Succeeded,

    /// <summary>The AI's answer did not validate twice, or the provider did not answer. A 201 all the same.</summary>
    Failed,
}
