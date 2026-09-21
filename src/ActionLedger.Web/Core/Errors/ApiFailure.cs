namespace ActionLedger.Web.Core.Errors;

/// <summary>
/// AD-14 — the web-owned shape of a failed request. This is what crosses the seam: a page can
/// render a problem title and branch on a status code without importing
/// <c>ActionLedger.Web.Core.Api</c>, which <c>WebStructureTests</c> forbids above <c>Core/</c> and
/// <c>Features/*/Data/</c>.
/// </summary>
/// <param name="StatusCode">
/// The HTTP status, or <c>0</c> when the request never reached a response — a transport failure
/// carries no status, and the difference matters to a caller deciding between a refusal and a
/// retry.
/// </param>
/// <param name="Title">The problem title, already defaulted, so it is never empty.</param>
/// <param name="Detail">The problem detail when the body carried one. Nothing renders it yet.</param>
public sealed record ApiFailure(int StatusCode, string Title, string? Detail);
