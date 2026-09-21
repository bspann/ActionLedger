using Xunit;

namespace ActionLedger.Application.Tests;

/// <summary>
/// Wiring placeholder for the Application ring (AD-18: Fake provider, in-memory repository fakes).
/// Handler and query tests land here from Story 1.4 onward.
/// </summary>
public sealed class ApplicationRingTests
{
    [Fact]
    public void Application_ring_is_present_and_loadable()
    {
        Assert.Equal("ActionLedger.Application", typeof(ApplicationAssemblyMarker).Assembly.GetName().Name);
    }
}
