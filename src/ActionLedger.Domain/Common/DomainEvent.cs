namespace ActionLedger.Domain.Common;

/// <summary>
/// Something an aggregate decided. Raised inside an aggregate method, collected by
/// <see cref="AggregateRoot"/>, and drained by the persistence layer inside the same commit
/// (AD-20) — never published from the aggregate itself.
/// </summary>
/// <remarks>
/// Naming is <c>&lt;Noun&gt;&lt;PastTenseVerb&gt;</c> (Consistency Conventions, Naming row). The
/// <c>User</c> aggregate raises none yet; the outbox rows that consume these arrive with the
/// webhook epic.
/// </remarks>
public abstract record DomainEvent
{
    /// <summary>When the aggregate decided it. Always supplied from <c>IClock</c>, never from <c>DateTimeOffset.UtcNow</c> (AD-15).</summary>
    public required DateTimeOffset OccurredAt { get; init; }
}
