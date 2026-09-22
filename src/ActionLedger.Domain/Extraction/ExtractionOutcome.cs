namespace ActionLedger.Domain.Extraction;

/// <summary>
/// AD-6 — how an Extraction Run ended. Every run records one, and both values are ordinary
/// outcomes: a failed extraction is not an HTTP error but a persisted run the caller is handed
/// with 201 (AD-11, Consistency Conventions, Errors row).
/// </summary>
/// <remarks>Stored as a string and serialized as a PascalCase string (Consistency Conventions, Enums row).</remarks>
public enum ExtractionOutcome
{
    /// <summary>The provider returned an answer that deserialized strictly and passed validation.</summary>
    Succeeded,

    /// <summary>
    /// Both attempts failed. The run carries a <c>FailureReason</c> and no proposals; the reason —
    /// trimmed, and clipped to 2,000 characters — is shown to a human beside a "Run again" affordance.
    /// </summary>
    Failed,
}
