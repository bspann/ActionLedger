using Xunit;

namespace ActionLedger.Infrastructure.Tests;

/// <summary>
/// Wiring placeholder for the Infrastructure ring (AD-18: Testcontainers PostgreSQL, real AppDbContext).
/// Testcontainers arrives with Story 1.3; this project must not need Docker before then.
/// </summary>
public sealed class InfrastructureRingTests
{
    [Fact]
    public void Infrastructure_ring_is_present_and_loadable()
    {
        Assert.Equal("ActionLedger.Infrastructure", typeof(InfrastructureAssemblyMarker).Assembly.GetName().Name);
    }
}
