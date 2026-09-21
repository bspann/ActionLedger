using System.ComponentModel.DataAnnotations;

namespace ActionLedger.Api.Configuration;

/// <summary>
/// The <c>Webhooks</c> section (AD-16). The defaults are the Timeouts row of the Consistency
/// Conventions and the FR-32 retry schedule: 10s, 30s, 2m, 10m, 30m, then Dead.
/// </summary>
public sealed class WebhooksOptions
{
    public const string SectionName = "Webhooks";

    [MinLength(1, ErrorMessage = "Webhooks:RetryDelaysSeconds must list at least one delay.")]
    public int[] RetryDelaysSeconds { get; init; } = [10, 30, 120, 600, 1800];

    [Range(1, 300, ErrorMessage = "Webhooks:TimeoutSeconds must be between 1 and 300.")]
    public int TimeoutSeconds { get; init; } = 10;

    [Range(1, 3600, ErrorMessage = "Webhooks:LeaseSeconds must be between 1 and 3600.")]
    public int LeaseSeconds { get; init; } = 60;
}
