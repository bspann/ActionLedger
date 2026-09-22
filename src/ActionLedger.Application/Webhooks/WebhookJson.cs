using System.Text.Json;
using System.Text.Json.Serialization;

namespace ActionLedger.Application.Webhooks;

/// <summary>
/// The options every outbox payload is serialized with: the API's own — web defaults, with enums
/// as PascalCase strings — so an integrator reads the same JSON the api answers with.
/// </summary>
/// <remarks>
/// <c>Api.Tests</c> pins this against the host's configured <c>JsonOptions</c>, so the two cannot
/// drift apart silently.
/// </remarks>
public static class WebhookJson
{
    /// <summary>Web defaults plus <see cref="JsonStringEnumConverter"/>. Shared and read-only.</summary>
    public static JsonSerializerOptions SerializerOptions { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web);

        options.Converters.Add(new JsonStringEnumConverter());
        options.MakeReadOnly(populateMissingResolver: true);

        return options;
    }
}
