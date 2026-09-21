namespace ActionLedger.Application.Abstractions;

/// <summary>
/// Another writer changed the aggregate between the read and the save. The Api maps this to
/// HTTP 409 with ProblemDetails <c>type: conflict</c> (AD-13 Errors row) — the same status and
/// type as <see cref="ActionLedger.Domain.Common.DomainRuleException"/>, because from the
/// caller's side both mean "your write did not apply; re-read and try again".
/// </summary>
/// <remarks>
/// Infrastructure translates the persistence provider's concurrency failure into this type so
/// that neither Application nor Api ever sees an EF Core exception (AD-1).
/// </remarks>
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(string message)
        : base(message)
    {
    }

    public ConcurrencyConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
