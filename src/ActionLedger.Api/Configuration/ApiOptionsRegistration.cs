using Microsoft.Extensions.Options;

namespace ActionLedger.Api.Configuration;

/// <summary>
/// AD-16 — every option group is bound, annotated, and validated before the host reports ready.
/// A missing or malformed key fails the host at startup with a message naming the key; there is
/// no partial start.
/// </summary>
public static class ApiOptionsRegistration
{
    /// <summary>Binds and validates <c>Ai</c>, <c>Jwt</c>, <c>Database</c>, <c>Webhooks</c>, and <c>Seed</c>.</summary>
    public static IServiceCollection AddApiOptions(this IServiceCollection services, IConfiguration configuration)
    {
        Bind<AiOptions>(services, configuration, AiOptions.SectionName);
        Bind<JwtOptions>(services, configuration, JwtOptions.SectionName);
        Bind<DatabaseOptions>(services, configuration, DatabaseOptions.SectionName);
        Bind<WebhooksOptions>(services, configuration, WebhooksOptions.SectionName);
        Bind<SeedOptions>(services, configuration, SeedOptions.SectionName);

        services.AddSingleton<IValidateOptions<AiOptions>, AiOptionsValidator>();

        return services;
    }

    private static void Bind<TOptions>(IServiceCollection services, IConfiguration configuration, string section)
        where TOptions : class =>
        services.AddOptions<TOptions>()
            .Bind(configuration.GetSection(section))
            .ValidateDataAnnotations()
            .ValidateOnStart();
}

/// <summary>
/// The one <c>Ai</c> rule data annotations cannot express: which sub-section is required depends
/// on <c>Ai:Provider</c>.
/// </summary>
/// <remarks>
/// AD-16 also calls for a provider *reachability* probe at startup — <c>GET {BaseUrl}/models</c>
/// against a local server, credential presence against Azure. That is a hosted service over the
/// AI ring, which does not exist until Epic 2. This validator covers the configuration shape,
/// which is all this story can honestly check.
/// </remarks>
internal sealed class AiOptionsValidator : IValidateOptions<AiOptions>
{
    public ValidateOptionsResult Validate(string? name, AiOptions options)
    {
        List<string> failures = [];

        switch (options.Provider)
        {
            case AiOptions.LocalOpenAIProvider:
                RequireKey(failures, options.Provider, "Ai:LocalOpenAI:BaseUrl", options.LocalOpenAI.BaseUrl);
                RequireKey(failures, options.Provider, "Ai:LocalOpenAI:Model", options.LocalOpenAI.Model);
                break;

            case AiOptions.AzureOpenAIProvider:
                RequireKey(failures, options.Provider, "Ai:AzureOpenAI:Endpoint", options.AzureOpenAI.Endpoint);
                RequireKey(failures, options.Provider, "Ai:AzureOpenAI:Model", options.AzureOpenAI.Model);
                RequireKey(failures, options.Provider, "Ai:AzureOpenAI:ApiKey", options.AzureOpenAI.ApiKey);
                break;

            default:
                // Fake needs nothing; an unrecognised provider is already a data-annotation failure.
                break;
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void RequireKey(List<string> failures, string provider, string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add($"{key} is required when Ai:Provider is {provider}.");
        }
    }
}
