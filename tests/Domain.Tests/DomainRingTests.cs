using Xunit;

namespace ActionLedger.Domain.Tests;

/// <summary>
/// Wiring placeholder for the Domain ring (AD-18: pure tests, no mocks).
/// Aggregate roots and transition rules are tested here from Epic 2 onward.
/// </summary>
public sealed class DomainRingTests
{
    [Fact]
    public void Domain_ring_is_present_and_loadable()
    {
        Assert.Equal("ActionLedger.Domain", typeof(DomainAssemblyMarker).Assembly.GetName().Name);
    }
}
