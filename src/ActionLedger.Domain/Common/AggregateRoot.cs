namespace ActionLedger.Domain.Common;

/// <summary>
/// The base every aggregate root in this solution inherits. It owns two things and nothing else:
/// the identity, and the domain events the root raised during the current unit of work.
/// </summary>
/// <remarks>
/// <para>
/// State changes happen only through methods on the root (AD-3, AD-20), so every setter on a
/// derived aggregate is private and every mutation that matters goes past a method that can
/// <see cref="Raise"/> an event and append an audit row in the same breath.
/// </para>
/// <para>
/// The id is a UUIDv7 created in the constructor — not by the database. The model-wide
/// <c>ValueGeneratedNever</c> convention in <c>AppDbContext</c> is what holds EF to that (AD-10).
/// </para>
/// </remarks>
public abstract class AggregateRoot
{
    private readonly List<DomainEvent> _domainEvents = [];

    /// <summary>Creates a root with a fresh UUIDv7 identity.</summary>
    protected AggregateRoot() => Id = Guid.CreateVersion7();

    /// <summary>The aggregate's identity: a UUIDv7 from <see cref="Guid.CreateVersion7()"/>.</summary>
    public Guid Id { get; private init; }

    /// <summary>The events raised since the last <see cref="ClearDomainEvents"/>.</summary>
    public IReadOnlyList<DomainEvent> DomainEvents => _domainEvents;

    /// <summary>Records an event for the persistence layer to drain inside the commit.</summary>
    protected void Raise(DomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    /// <summary>Drops the collected events. Called once the commit that carried them succeeded.</summary>
    public void ClearDomainEvents() => _domainEvents.Clear();
}
