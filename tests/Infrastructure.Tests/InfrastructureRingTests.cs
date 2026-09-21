using Xunit;

namespace ActionLedger.Infrastructure.Tests;

/// <summary>
/// The ring loads at all. Everything else in this project runs against a real PostgreSQL through
/// <see cref="PostgresFixture"/> (AD-18), so this is the one test here that needs no Docker — and
/// the one that still says something useful when Docker is what broke.
/// </summary>
public sealed class InfrastructureRingTests
{
    [Fact]
    public void Infrastructure_ring_is_present_and_loadable()
    {
        Assert.Equal("ActionLedger.Infrastructure", typeof(InfrastructureAssemblyMarker).Assembly.GetName().Name);
    }
}
