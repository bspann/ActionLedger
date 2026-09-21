namespace ActionLedger.Api.Health;

/// <summary>
/// The body both health endpoints answer with. A named record rather than an anonymous object so
/// the contract — and the client generated from it — carries a real type.
/// </summary>
/// <param name="Status">
/// <c>healthy</c> for liveness; <c>ready</c> or <c>unavailable</c> for readiness. Liveness only
/// ever answers <c>healthy</c>: reaching the endpoint at all is the signal.
/// </param>
public sealed record HealthStatus(string Status)
{
    /// <summary>Liveness: the process is serving.</summary>
    public static HealthStatus Healthy { get; } = new("healthy");

    /// <summary>Readiness: the database answered.</summary>
    public static HealthStatus Ready { get; } = new("ready");

    /// <summary>Readiness: the database did not answer. Served with 503, never 200 (AD-17).</summary>
    public static HealthStatus Unavailable { get; } = new("unavailable");
}
