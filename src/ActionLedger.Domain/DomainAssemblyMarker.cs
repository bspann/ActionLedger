namespace ActionLedger.Domain;

/// <summary>
/// Compile-time handle on the Domain assembly. Architecture.Tests and the ring test projects
/// resolve the real compiled assembly through this type rather than by name lookup.
/// Aggregate roots, entities, and value objects arrive with Epic 2 onward.
/// </summary>
public sealed class DomainAssemblyMarker;
