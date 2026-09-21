using Xunit;

namespace ActionLedger.Api.Tests;

/// <summary>
/// Wiring placeholder for the Api ring (AD-18: WebApplicationFactory, auth, ProblemDetails,
/// OpenApiSnapshotTest). Those arrive with Story 1.2; nothing here starts a host yet.
/// </summary>
public sealed class ApiRingTests
{
    [Fact]
    public void Api_ring_is_present_and_loadable()
    {
        Assert.Equal("ActionLedger.Api", typeof(ApiAssemblyMarker).Assembly.GetName().Name);
    }
}
