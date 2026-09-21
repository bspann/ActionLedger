using System.Net;
using ActionLedger.Api.Configuration;
using ActionLedger.Api.OpenApi;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace ActionLedger.Api.Tests;

/// <summary>
/// AD-13 — Swagger UI is served when the environment is <c>Development</c> or <c>Compose</c>,
/// and nowhere else. The OpenAPI document itself is always served: nginx proxies <c>/openapi</c>
/// and the contract is what the web client is generated from.
/// </summary>
public sealed class SwaggerExposureTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData(ApiEnvironments.Compose)]
    public async Task Swagger_ui_is_served_in_the_two_environments_that_ask_for_it(string environment)
    {
        await using TestApi api = new() { Environment = environment };
        using HttpClient client = api.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/swagger/index.html", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Swagger_ui_is_not_mapped_in_production()
    {
        await using TestApi api = new() { Environment = Environments.Production };
        using HttpClient client = api.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/swagger/index.html", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_openapi_document_is_served_over_http_in_every_environment()
    {
        await using TestApi api = new() { Environment = Environments.Production };
        using HttpClient client = api.CreateClient();

        HttpResponseMessage response = await client.GetAsync(
            $"/openapi/{OpenApiSetup.DocumentName}.json",
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
    }
}
