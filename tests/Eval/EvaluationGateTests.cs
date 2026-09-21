using ActionLedger.Infrastructure;
using Xunit;

namespace ActionLedger.Eval;

/// <summary>
/// Wiring placeholder for the Evaluation Gate (AD-19). The deterministic scorer,
/// thresholds.json, and the fixture catalog arrive with Story 6.2; this project runs
/// the extractor through the same Infrastructure code as the API and never calls a model here.
/// </summary>
public sealed class EvaluationGateTests
{
    [Fact]
    public void Eval_project_reaches_the_infrastructure_ring_it_will_score()
    {
        Assert.Equal("ActionLedger.Infrastructure", typeof(InfrastructureAssemblyMarker).Assembly.GetName().Name);
    }
}
