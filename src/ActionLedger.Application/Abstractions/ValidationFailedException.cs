namespace ActionLedger.Application.Abstractions;

/// <summary>
/// The request is well-formed but cannot proceed against the current state. The Api maps this to
/// HTTP 400 with ProblemDetails <c>type: validation</c> (AD-13 Errors row).
/// </summary>
/// <remarks>
/// <para>
/// It is deliberately distinct from <see cref="NotFoundException"/> and from
/// <c>DomainRuleException</c>. "Run extraction on a Meeting with no notes" names a Meeting that
/// really is there, and asking again later could succeed — so it is neither a 404 nor the 409 a
/// broken invariant earns (FR-4, <c>prd.md:143</c>).
/// </para>
/// <para>
/// Model binding answers a malformed body with its own 400 before a handler runs. This is for what
/// only a handler can know, after it has loaded something.
/// </para>
/// </remarks>
public sealed class ValidationFailedException : Exception
{
    public ValidationFailedException(string message)
        : base(message)
    {
    }

    public ValidationFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
