using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ActionLedger.Infrastructure.Persistence;

/// <summary>
/// AD-17 — what <c>GET /health/ready</c> asks. Liveness answers from the process alone; readiness
/// is only true when the database actually answers.
/// </summary>
/// <remarks>
/// It opens a connection rather than inspecting configuration, and it turns every failure into a
/// <c>false</c> so an unreachable database is a 503, never an unhandled exception and never a
/// 500. The reason is logged once, at warning, so an operator can see it without the probe
/// describing the connection to the caller.
/// </remarks>
public sealed class DatabaseReadiness(AppDbContext context, ILogger<DatabaseReadiness> logger)
{
    /// <summary>
    /// How long the probe waits before answering "not ready".
    /// </summary>
    /// <remarks>
    /// The context is configured to retry transient connection failures, which is right for a
    /// query and wrong for a probe: a readiness check that keeps retrying is a readiness check
    /// that never answers. The cap bounds the whole attempt, retries included.
    /// </remarks>
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    /// <summary>True when the database answered within <see cref="ProbeTimeout"/>.</summary>
    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken = default)
    {
        using CancellationTokenSource bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        bounded.CancelAfter(ProbeTimeout);

        try
        {
            return await context.Database.CanConnectAsync(bounded.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                "Readiness probe gave up after {ProbeTimeoutSeconds}s: the database did not answer.",
                ProbeTimeout.TotalSeconds);

            return false;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Readiness probe could not reach the database.");

            return false;
        }
    }
}
