using ActionLedger.Application.Abstractions;
using ActionLedger.Application.Ai;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ActionLedger.Api.Controllers;

/// <summary>
/// FR-7 / AD-11 — <c>ai/provider</c>, the read-only report of which AI Provider is wired up. The
/// route carries no <c>api/v1</c>: <c>ApiRoutePrefixConvention</c> prepends it, so a controller
/// cannot forget the prefix.
/// </summary>
/// <remarks>
/// <para>
/// The values come from <see cref="IAiProviderInfo"/>, which the ring that built the chat client
/// fills in. Nothing here reads configuration, and nothing a caller could use to reach the provider
/// — an endpoint, a key, a base URL — is ever published (NFR-4).
/// </para>
/// <para>
/// AD-12 — reads are open to any authenticated User, so a bare <c>[Authorize]</c> is the whole
/// rule.
/// </para>
/// </remarks>
[ApiController]
[Route("ai")]
[Authorize]
[Tags("Ai")]
public sealed class AiController(IAiProviderInfo providerInfo) : ControllerBase
{
    /// <summary>Reports the active AI provider and model.</summary>
    /// <remarks>
    /// The provider is the configured <c>Ai:Provider</c> and the model is what that provider
    /// reports. Meeting Detail names both while a run is in flight. Changing either takes a
    /// configuration change and a restart; nothing here writes.
    /// </remarks>
    /// <response code="200">The active provider and model.</response>
    /// <response code="401">No token, or a token this Api did not issue.</response>
    [HttpGet("provider")]
    [EndpointName("GetAiProvider")]
    [EndpointSummary("Reports the active AI provider and model.")]
    [EndpointDescription(
        "provider is the configured Ai:Provider (Fake, LocalOpenAI or AzureOpenAI) and model is "
        + "what that provider reports. No endpoint, key or base URL is published.")]
    [ProducesResponseType<AiProviderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public ActionResult<AiProviderDto> GetProvider() =>
        Ok(new AiProviderDto(providerInfo.Provider, providerInfo.Model));
}
