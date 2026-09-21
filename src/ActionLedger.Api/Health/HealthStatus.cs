namespace ActionLedger.Api.Health;

/// <summary>
/// The liveness body. A named record rather than an anonymous object so the contract — and the
/// client generated from it — carries a real type.
/// </summary>
/// <param name="Status">Always <c>healthy</c>: reaching this endpoint at all is the signal.</param>
public sealed record HealthStatus(string Status)
{
    public static HealthStatus Healthy { get; } = new("healthy");
}
