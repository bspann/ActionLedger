using System.ClientModel.Primitives;
using OpenAI;

namespace ActionLedger.Infrastructure.Ai.Providers;

/// <summary>
/// The OpenAI SDK options both real providers share. LocalOpenAI and AzureOpenAI are the same SDK
/// differing only in endpoint, model, and credential, so the transport rules live once.
/// </summary>
/// <remarks>
/// <para>
/// <strong>No SDK retries.</strong> <c>ChatClientActionExtractor</c> owns retry-once (AD-11). With
/// the SDK's default of three retries one extractor "attempt" could hide four HTTP calls, and a
/// run could outlive NFR-1's 180-second ceiling many times over. With zero, one attempt is one call.
/// </para>
/// <para>
/// <strong>The SDK timeout sits above the budget.</strong> The extractor bounds each call with
/// <c>Ai:CallTimeoutSeconds</c>. The SDK's own <c>NetworkTimeout</c> is that plus ten seconds, so
/// the budget token always fires first and the failure reads as the extractor's timeout rather than
/// as an SDK cancellation it cannot tell apart (DW-17).
/// </para>
/// </remarks>
internal static class OpenAIWire
{
    /// <summary>How far past the per-call budget the SDK's own timeout sits.</summary>
    internal static readonly TimeSpan NetworkTimeoutMargin = TimeSpan.FromSeconds(10);

    /// <summary>The client options for one provider endpoint.</summary>
    /// <param name="endpoint">The provider's base URL.</param>
    /// <param name="networkTimeout">The SDK's own per-request timeout.</param>
    internal static OpenAIClientOptions Options(Uri endpoint, TimeSpan networkTimeout) =>
        new()
        {
            Endpoint = endpoint,
            NetworkTimeout = networkTimeout,
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
        };

    /// <summary>The SDK timeout for a chat call under <paramref name="settings"/>' budget.</summary>
    internal static TimeSpan ChatNetworkTimeout(AiSettings settings) =>
        TimeSpan.FromSeconds(settings.CallTimeoutSeconds) + NetworkTimeoutMargin;

    /// <summary>
    /// <paramref name="value"/> as an absolute URI whose scheme is one of <paramref name="schemes"/>,
    /// or null. The one parse both factories and both probes use.
    /// </summary>
    internal static Uri? AbsoluteUri(string? value, params string[] schemes) =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && schemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase)
            ? uri
            : null;
}
