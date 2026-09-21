namespace ActionLedger.Domain.Common;

/// <summary>
/// A Domain invariant was violated — an invalid state transition, a rule an aggregate root
/// refuses to break. The Api maps this to HTTP 409 with ProblemDetails <c>type: conflict</c>
/// (AD-13 Errors row).
/// </summary>
/// <remarks>
/// Domain throws this; Domain never knows it becomes a 409. The mapping lives in
/// <c>ActionLedger.Api.Errors</c> and is the only place that knows about HTTP.
/// </remarks>
public sealed class DomainRuleException : Exception
{
    public DomainRuleException(string message)
        : base(message)
    {
    }

    public DomainRuleException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
