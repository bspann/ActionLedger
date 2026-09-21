using System.Text.Json;
using ActionLedger.Api.OpenApi;
using ActionLedger.Application.Abstractions;
using ActionLedger.Domain.Users;
using Xunit;

namespace ActionLedger.Api.Tests;

/// <summary>
/// The parts of the contract that no endpoint pins down yet, and so could rot unnoticed between
/// here and the story that first uses them.
/// </summary>
public sealed class OpenApiContractTests
{
    private static async Task<JsonElement> ContractAsync()
    {
        await using TestApi api = new();
        using HttpClient _ = api.CreateClient();

        string json = await OpenApiExport.GenerateAsync(api.Services, TestContext.Current.CancellationToken);

        return JsonDocument.Parse(json).RootElement.Clone();
    }

    [Fact]
    public async Task Paged_result_component_matches_the_application_type()
    {
        JsonElement contract = await ContractAsync();

        JsonElement component = contract
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty(OpenApiSetup.PagedResultComponent);

        string[] documented = [.. component.GetProperty("properties").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)];

        // The envelope is published as a component without an operation referencing it, so nothing
        // else would notice if PagedResult<T> and the component drifted apart.
        string[] actual =
        [
            .. typeof(PagedResult<object>)
                .GetProperties()
                .Select(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name))
                .Order(StringComparer.Ordinal),
        ];

        Assert.Equal(actual, documented);

        string[] required = [.. component.GetProperty("required").EnumerateArray().Select(value => value.GetString()!).Order(StringComparer.Ordinal)];

        Assert.Equal(actual, required);
    }

    [Fact]
    public async Task Paging_parameters_carry_the_documented_defaults_and_bounds()
    {
        JsonElement contract = await ContractAsync();

        JsonElement parameters = contract.GetProperty("components").GetProperty("parameters");

        JsonElement page = parameters.GetProperty("page").GetProperty("schema");
        Assert.Equal(Paging.FirstPage, page.GetProperty("default").GetInt32());

        JsonElement pageSize = parameters.GetProperty("pageSize").GetProperty("schema");
        Assert.Equal(Paging.DefaultPageSize, pageSize.GetProperty("default").GetInt32());
        Assert.Equal(Paging.MaxPageSize, pageSize.GetProperty("maximum").GetInt32());
    }

    [Fact]
    public async Task Bearer_scheme_is_published_for_the_operations_story_1_4_adds()
    {
        JsonElement contract = await ContractAsync();

        JsonElement bearer = contract
            .GetProperty("components")
            .GetProperty("securitySchemes")
            .GetProperty(OpenApiSetup.BearerSchemeId);

        Assert.Equal("http", bearer.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());
        Assert.Equal("JWT", bearer.GetProperty("bearerFormat").GetString());
    }

    [Fact]
    public async Task Enums_are_published_as_the_strings_they_are_serialized_as()
    {
        JsonElement contract = await ContractAsync();

        JsonElement role = contract
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty(nameof(Role));

        // The document generator does not read the MVC serializer's settings, so without a
        // transformer this is published as a bare integer while the wire carries "ActionOfficer" —
        // and Story 1.5's client, generated from this file, would type `role` as a number.
        Assert.Equal("string", role.GetProperty("type").GetString());

        string[] published = [.. role.GetProperty("enum").EnumerateArray().Select(value => value.GetString()!)];

        // Pinned to the Domain enum, so adding or renaming a Role fails here until the contract
        // is re-exported rather than drifting silently.
        Assert.Equal(Enum.GetNames<Role>(), published);
    }

    [Fact]
    public async Task Contract_declares_no_server_so_it_does_not_depend_on_the_port_it_was_generated_on()
    {
        JsonElement contract = await ContractAsync();

        Assert.False(contract.TryGetProperty("servers", out _));
    }
}
