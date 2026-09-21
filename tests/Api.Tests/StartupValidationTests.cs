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

    [Fact]
    public async Task A_complete_configuration_starts()
    {
        await using TestApi api = new();
        using HttpClient client = api.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
    }
}
