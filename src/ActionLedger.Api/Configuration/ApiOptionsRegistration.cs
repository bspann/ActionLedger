using ActionLedger.Domain.Extraction;
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
        services.AddSingleton<IValidateOptions<SeedOptions>, SeedOptionsValidator>();

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
/// The <c>Ai</c> rules data annotations cannot express: which sub-section is required, and what
/// shape its values must have, depends on <c>Ai:Provider</c>.
/// </summary>
/// <remarks>
/// <para>
/// Only the active provider's section is checked — the others are legitimately empty. For it: every
/// key is present, the base URL or endpoint is an absolute <c>http</c>/<c>https</c> URI (Azure's
/// must be <c>https</c>), and the model name fits <c>ExtractionRunMetadata.ModelMaxLength</c>, so a
/// run can never be refused at persistence for a model name configuration allowed (DW-21).
/// </para>
/// <para>
/// This is shape only. Reachability — <c>GET {BaseUrl}/models</c> — and Azure credential presence
/// are Infrastructure's <c>ProviderStartupProbe</c>, which starts after <c>ValidateOnStart</c>, so a
/// malformed URL reads like every other configuration error: an
/// <see cref="OptionsValidationException"/> naming the key.
/// </para>
/// </remarks>
internal sealed class AiOptionsValidator : IValidateOptions<AiOptions>
{
    public ValidateOptionsResult Validate(string? name, AiOptions options)
    {
        List<string> failures = [];

        switch (options.Provider)
        {
            case AiOptions.LocalOpenAIProvider:
                RequireUri(failures, options.Provider, "Ai:LocalOpenAI:BaseUrl", options.LocalOpenAI.BaseUrl, httpsOnly: false);
                RequireModel(failures, options.Provider, "Ai:LocalOpenAI:Model", options.LocalOpenAI.Model);
                break;

            case AiOptions.AzureOpenAIProvider:
                RequireUri(failures, options.Provider, "Ai:AzureOpenAI:Endpoint", options.AzureOpenAI.Endpoint, httpsOnly: true);
                RequireModel(failures, options.Provider, "Ai:AzureOpenAI:Model", options.AzureOpenAI.Model);
                RequireKey(failures, options.Provider, "Ai:AzureOpenAI:ApiKey", options.AzureOpenAI.ApiKey);
                break;

            default:
                // Fake needs nothing; an unrecognised provider is already a data-annotation failure.
                break;
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static bool RequireKey(List<string> failures, string provider, string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add($"{key} is required when Ai:Provider is {provider}.");

            return false;
        }

        return true;
    }

    private static void RequireUri(List<string> failures, string provider, string key, string value, bool httpsOnly)
    {
        if (!RequireKey(failures, provider, key, value))
        {
            return;
        }

        bool valid = Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            && (uri.Scheme == Uri.UriSchemeHttps || (!httpsOnly && uri.Scheme == Uri.UriSchemeHttp));

        if (!valid)
        {
            failures.Add(httpsOnly
                ? $"{key} must be an absolute https URL when Ai:Provider is {provider}, for example https://<resource>.openai.azure.com/openai/v1/."
                : $"{key} must be an absolute http or https URL when Ai:Provider is {provider}, for example http://host.docker.internal:1234/v1.");
        }
    }

    private static void RequireModel(List<string> failures, string provider, string key, string value)
    {
        if (RequireKey(failures, provider, key, value) && value.Length > ExtractionRunMetadata.ModelMaxLength)
        {
            failures.Add($"{key} must be at most {ExtractionRunMetadata.ModelMaxLength} characters; every run records it.");
        }
    }
}

/// <summary>
/// The one <c>Seed</c> rule data annotations cannot express: the demo password is required only
/// when seeding is on.
/// </summary>
/// <remarks>
/// NFR5 keeps the value itself out of the repository, so there is no default to fall back to —
/// which is the point. A host asked to seed without a password stops here, naming the key, rather
/// than creating accounts whose password is in the source.
/// </remarks>
internal sealed class SeedOptionsValidator : IValidateOptions<SeedOptions>
{
    public ValidateOptionsResult Validate(string? name, SeedOptions options)
    {
        if (!options.Enabled || !string.IsNullOrWhiteSpace(options.DefaultPassword))
        {
            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail(
            $"{SeedOptions.DefaultPasswordKey} is required when {SeedOptions.SectionName}:Enabled is true. "
            + "Supply it from the environment or user secrets; there is no default.");
    }
}
