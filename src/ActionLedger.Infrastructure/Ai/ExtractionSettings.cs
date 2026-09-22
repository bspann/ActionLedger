using ActionLedger.Application.Abstractions;

namespace ActionLedger.Infrastructure.Ai;

/// <summary>
/// AD-16 — <see cref="IExtractionSettings"/> "implemented in Infrastructure over the options".
/// The Application ring reads the threshold through the port and never sees <c>IOptions</c>.
/// </summary>
/// <remarks>
/// It is a projection of <see cref="AiSettings"/> rather than a second reader of configuration, so
/// there is still exactly one definition of <c>Ai:LowConfidenceThreshold</c> and one place it is
/// validated — <c>AiOptions</c>' <c>[Range(0.0, 1.0)]</c>, at startup.
/// </remarks>
internal sealed class ExtractionSettings(AiSettings settings) : IExtractionSettings
{
    public double LowConfidenceThreshold => settings.LowConfidenceThreshold;
}
