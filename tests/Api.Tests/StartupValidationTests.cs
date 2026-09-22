using Microsoft.Extensions.Options;
using Xunit;

namespace ActionLedger.Api.Tests;

/// <summary>
/// AD-16 — a required configuration key that is missing or malformed fails the host at startup
/// with a message naming the key. There is no partial start and no first-request surprise.
/// </summary>
public sealed class StartupValidationTests
{
    [Theory]
    [InlineData("Jwt:Key")]
    [InlineData("Jwt:Issuer")]
    [InlineData("Database:ConnectionString")]
    [InlineData("Ai:Provider")]
    [InlineData("Ai:PromptVersion")]
    public async Task A_missing_required_key_fails_the_host_and_the_message_names_it(string key)
    {
        await using TestApi api = new() { ConfigurationOverrides = { [key] = null } };

        OptionsValidationException failure =
            await Assert.ThrowsAsync<OptionsValidationException>(() => Task.Run(() => api.CreateClient(), TestContext.Current.CancellationToken));

        Assert.Contains(key, string.Join(" ", failure.Failures), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_signing_key_too_short_for_hs256_fails_the_host()
    {
        await using TestApi api = new() { ConfigurationOverrides = { ["Jwt:Key"] = "too-short" } };

        OptionsValidationException failure =
            await Assert.ThrowsAsync<OptionsValidationException>(() => Task.Run(() => api.CreateClient(), TestContext.Current.CancellationToken));

        Assert.Contains("Jwt:Key", string.Join(" ", failure.Failures), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unrecognised_ai_provider_fails_the_host()
    {
        await using TestApi api = new() { ConfigurationOverrides = { ["Ai:Provider"] = "Skynet" } };

        OptionsValidationException failure =
            await Assert.ThrowsAsync<OptionsValidationException>(() => Task.Run(() => api.CreateClient(), TestContext.Current.CancellationToken));

        Assert.Contains("Ai:Provider", string.Join(" ", failure.Failures), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_provider_whose_sub_section_is_empty_fails_the_host_naming_the_sub_key()
    {
        await using TestApi api = new() { ConfigurationOverrides = { ["Ai:Provider"] = "LocalOpenAI" } };

        OptionsValidationException failure =
            await Assert.ThrowsAsync<OptionsValidationException>(() => Task.Run(() => api.CreateClient(), TestContext.Current.CancellationToken));

        Assert.Contains("Ai:LocalOpenAI:BaseUrl", string.Join(" ", failure.Failures), StringComparison.Ordinal);
    }

    /// <summary>
    /// AD-16, DW-16, DW-21 — the active provider's section is complete, well-formed, and fits the
    /// run record, or the host does not start. Each row names the key that is wrong.
    /// </summary>
    [Theory]
    [InlineData("AzureOpenAI", "Ai:AzureOpenAI:Endpoint", null)]
    [InlineData("AzureOpenAI", "Ai:AzureOpenAI:Model", null)]
    [InlineData("AzureOpenAI", "Ai:AzureOpenAI:ApiKey", null)]
    [InlineData("AzureOpenAI", "Ai:AzureOpenAI:Endpoint", "not-a-url")]
    [InlineData("AzureOpenAI", "Ai:AzureOpenAI:Endpoint", "http://example-resource.openai.azure.com/openai/v1/")]
    [InlineData("AzureOpenAI", "Ai:AzureOpenAI:Model", LongModel)]
    [InlineData("LocalOpenAI", "Ai:LocalOpenAI:BaseUrl", "not-a-url")]
    [InlineData("LocalOpenAI", "Ai:LocalOpenAI:BaseUrl", "ftp://localhost:1234/v1")]
    [InlineData("LocalOpenAI", "Ai:LocalOpenAI:Model", null)]
    [InlineData("LocalOpenAI", "Ai:LocalOpenAI:Model", LongModel)]
    [InlineData("Fake", "Ai:CallTimeoutSeconds", "91")]
    public async Task A_malformed_active_provider_section_fails_the_host_naming_the_key(string provider, string key, string? value)
    {
        Dictionary<string, string?> overrides = ProviderSection(provider);
        overrides[key] = value;

        await using TestApi api = new() { ConfigurationOverrides = overrides };

        OptionsValidationException failure =
            await Assert.ThrowsAsync<OptionsValidationException>(() => Task.Run(() => api.CreateClient(), TestContext.Current.CancellationToken));

        Assert.Contains(key, string.Join(" ", failure.Failures), StringComparison.Ordinal);
    }

    /// <summary>Only the active provider's section is checked: a malformed inactive one is not a failure.</summary>
    [Fact]
    public async Task An_inactive_provider_section_is_not_validated()
    {
        await using TestApi api = new()
        {
            ConfigurationOverrides =
            {
                ["Ai:LocalOpenAI:BaseUrl"] = "not-a-url",
                ["Ai:AzureOpenAI:Endpoint"] = "http://insecure.example/",
                ["Ai:AzureOpenAI:Model"] = LongModel,
            },
        };

        using HttpClient client = api.CreateClient();

        (await client.GetAsync("/health", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
    }

    /// <summary>90 is the ceiling itself, and is allowed.</summary>
    [Fact]
    public async Task A_call_timeout_of_ninety_seconds_starts()
    {
        await using TestApi api = new() { ConfigurationOverrides = { ["Ai:CallTimeoutSeconds"] = "90" } };

        using HttpClient client = api.CreateClient();

        (await client.GetAsync("/health", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
    }

    /// <summary>DW-21 — a model name exactly at <c>ExtractionRunMetadata.ModelMaxLength</c> is allowed.</summary>
    [Fact]
    public async Task A_model_name_at_the_length_bound_starts()
    {
        Dictionary<string, string?> overrides = ProviderSection("AzureOpenAI");
        overrides["Ai:AzureOpenAI:Model"] = new string('m', ActionLedger.Domain.Extraction.ExtractionRunMetadata.ModelMaxLength);

        await using TestApi api = new() { ConfigurationOverrides = overrides };

        using HttpClient client = api.CreateClient();

        (await client.GetAsync("/health", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
    }

    /// <summary>
    /// A model name one character longer than <c>ExtractionRunMetadata.ModelMaxLength</c> (200),
    /// which every run records.
    /// </summary>
    private const string LongModel =
        "mmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmm"
        + "mmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmmm"
        + "m";

    /// <summary>A complete, valid section for <paramref name="provider"/>, which each theory row then breaks one key of.</summary>
    private static Dictionary<string, string?> ProviderSection(string provider) => provider switch
    {
        "AzureOpenAI" => new(StringComparer.Ordinal)
        {
            ["Ai:Provider"] = "AzureOpenAI",
            ["Ai:AzureOpenAI:Endpoint"] = "https://example-resource.openai.azure.com/openai/v1/",
            ["Ai:AzureOpenAI:Model"] = "gpt-4o-mini",
            ["Ai:AzureOpenAI:ApiKey"] = "not-a-real-key",
        },
        "LocalOpenAI" => new(StringComparer.Ordinal)
        {
            ["Ai:Provider"] = "LocalOpenAI",
            ["Ai:LocalOpenAI:BaseUrl"] = "http://127.0.0.1:1/v1",
            ["Ai:LocalOpenAI:Model"] = "any-model",
        },
        _ => new(StringComparer.Ordinal),
    };

    [Fact]
    public async Task Seeding_without_a_password_fails_the_host_naming_the_key()
    {
        // NFR5 — the demo password comes from the environment or user secrets and has no default
        // anywhere in the repository. A host told to seed without one has to stop and say so; the
        // failure it must never have is starting anyway with a credential someone can read here.
        await using TestApi api = new()
        {
            ConfigurationOverrides =
            {
                ["Seed:Enabled"] = "true",
                ["Seed:DefaultPassword"] = null,
            },
        };

        OptionsValidationException failure =
            await Assert.ThrowsAsync<OptionsValidationException>(() => Task.Run(() => api.CreateClient(), TestContext.Current.CancellationToken));

        Assert.Contains("Seed:DefaultPassword", string.Join(" ", failure.Failures), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Seeding_off_needs_no_password()
    {
        // The key is required only when it is going to be used, so cd.yml and every test that
        // does not want demo data keep booting without one.
        await using TestApi api = new()
        {
            ConfigurationOverrides =
            {
                ["Seed:Enabled"] = "false",
                ["Seed:DefaultPassword"] = null,
            },
        };

        using HttpClient client = api.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_complete_configuration_starts()
    {
        await using TestApi api = new();
        using HttpClient client = api.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
    }
}
