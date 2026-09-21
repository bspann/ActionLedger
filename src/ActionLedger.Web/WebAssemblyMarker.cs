namespace ActionLedger.Web;

/// <summary>
/// Compile-time handle on the web assembly, mirroring <c>ApiAssemblyMarker</c>. The AD-14 rule in
/// <c>Architecture.Tests</c> reaches this assembly by it, and <c>App.razor</c> hands it to the
/// router as the assembly to scan for routable components.
/// </summary>
public sealed class WebAssemblyMarker;
