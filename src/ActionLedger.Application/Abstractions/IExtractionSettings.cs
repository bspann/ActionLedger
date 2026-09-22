namespace ActionLedger.Application.Abstractions;

/// <summary>
/// AD-16 — Application reads settings only through ports. This one is implemented in
/// Infrastructure over the validated <c>Ai</c> options, so nothing in this ring ever sees
/// <c>IOptions</c> and there is still one definition of the key.
/// </summary>
/// <remarks>
/// The port carries the threshold and nothing else. The prompt version already reaches this ring on
/// <c>ExtractionMetrics</c>, so putting it here too would be a second source for one fact.
/// </remarks>
public interface IExtractionSettings
{
    /// <summary>
    /// <c>Ai:LowConfidenceThreshold</c> (default <c>0.70</c>). A proposal is flagged when its
    /// confidence is <em>strictly below</em> this, so a proposal exactly at the threshold is not
    /// flagged: the threshold is the first value that is not low.
    /// </summary>
    /// <remarks>
    /// <c>double</c>, not <c>decimal</c>, and deliberately. <c>AiOptions.LowConfidenceThreshold</c>
    /// is <c>double</c> and <c>ExtractedProposal.Confidence</c> is <c>double</c>; a <c>decimal</c>
    /// here would put a lossy cast on the one comparison that matters.
    /// </remarks>
    double LowConfidenceThreshold { get; }
}
