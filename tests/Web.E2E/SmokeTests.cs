using Xunit;

namespace ActionLedger.Web.E2E;

/// <summary>
/// Wiring placeholder for the browser suite (AD-18: one Playwright test of UJ-1 against
/// compose with the Fake provider). Playwright and the compose environment arrive with
/// Stories 1.7 and 6.4 — this project must not need a browser, a container, or a network today.
/// </summary>
public sealed class SmokeTests
{
    [Fact]
    public void Web_e2e_project_is_wired_into_the_test_matrix()
    {
        Assert.Equal("Web.E2E", typeof(SmokeTests).Assembly.GetName().Name);
    }
}
