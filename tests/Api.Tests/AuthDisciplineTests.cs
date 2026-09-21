using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ActionLedger.Api.OpenApi;
using ActionLedger.Api.Routing;
using Xunit;

namespace ActionLedger.Api.Tests;

/// <summary>
/// AD-12 — every operation in the published contract rejects an anonymous caller, except sign-in
/// and the health probes. Walking the generated document is what makes the rule apply to routes
/// nobody has written yet.
/// </summary>
/// <remarks>
/// The walk reads the document but asserts over HTTP on purpose. A doc-only "every operation
/// declares <c>security</c>" test is vacuous in the one case that matters: a controller that
/// forgot <c>[Authorize]</c> is both unsecured and documented as unsecured, so the two agree and
/// the assertion passes. Issuing the real request is what catches it.
/// </remarks>
public sealed class AuthDisciplineTests
{
    /// <summary>The one versioned operation an anonymous caller is allowed to reach.</summary>
    private static readonly string LoginPath = $"/{ApiRoutes.Prefix}/auth/login";

    private static readonly string[] Methods = ["get", "put", "post", "delete", "options", "head", "patch", "trace"];

    [Fact]
    public async Task Every_documented_operation_but_login_and_health_refuses_an_anonymous_caller()
    {
        await using TestApi api = new();
        using HttpClient client = api.CreateClient();

        List<string> walked = [];

        foreach ((string path, string method) in await OperationsAsync(api))
        {
            if (path.Equals(LoginPath, StringComparison.Ordinal) || IsUnversioned(path))
            {
                continue;
            }

            using HttpRequestMessage request = new(new HttpMethod(method.ToUpperInvariant()), Concrete(path));

            if (method is "post" or "put" or "patch")
            {
                request.Content = JsonContent.Create(new { });
            }

            HttpResponseMessage response = await client.SendAsync(request, TestContext.Current.CancellationToken);

            walked.Add($"{method.ToUpperInvariant()} {path}");

            // Not 400, not 404, not 500: authorization runs before the endpoint does, so a missing
            // token is answered before a body is ever looked at.
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        // A walk over nothing passes every assertion it never made. This is what stops that.
        Assert.NotEmpty(walked);
    }

    [Fact]
    public async Task Sign_in_is_published_without_a_security_requirement()
    {
        await using TestApi api = new();
        using HttpClient client = api.CreateClient();

        JsonElement login = (await ContractAsync(api)).GetProperty("paths").GetProperty(LoginPath).GetProperty("post");

        // [AllowAnonymous] is what RequireBearerWhereAuthorizedAsync reads. If it ever became an
        // authenticated operation the generated client would start demanding a token to get one.
        Assert.False(login.TryGetProperty("security", out JsonElement _));
    }

    [Fact]
    public async Task The_roster_is_published_as_requiring_the_bearer_scheme()
    {
        await using TestApi api = new();
        using HttpClient _ = api.CreateClient();

        JsonElement roster = (await ContractAsync(api))
            .GetProperty("paths")
            .GetProperty($"/{ApiRoutes.Prefix}/users")
            .GetProperty("get");

        string scheme = roster
            .GetProperty("security")[0]
            .EnumerateObject()
            .Single()
            .Name;

        Assert.Equal(OpenApiSetup.BearerSchemeId, scheme);
    }

    [Fact]
    public async Task The_roster_reuses_the_published_paging_parameters_rather_than_redeclaring_them()
    {
        await using TestApi api = new();
        using HttpClient client = api.CreateClient();

        JsonElement roster = (await ContractAsync(api))
            .GetProperty("paths")
            .GetProperty($"/{ApiRoutes.Prefix}/users")
            .GetProperty("get");

        // The snapshot test only byte-compares the contract against a file this story regenerated,
        // so dropping the transformer that makes these references would leave it green. This is
        // what actually holds the paging vocabulary to one definition.
        string[] references =
        [
            .. roster.GetProperty("parameters")
                .EnumerateArray()
                .Select(parameter => parameter.GetProperty("$ref").GetString()!),
        ];

        string[] expected =
        [
            .. OpenApiSetup.PagingParameterComponents.Select(name => $"#/components/parameters/{name}"),
        ];

        Assert.Equal(expected, references);
    }

    private static async Task<JsonElement> ContractAsync(TestApi api)
    {
        string json = await OpenApiExport.GenerateAsync(api.Services, TestContext.Current.CancellationToken);

        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static async Task<IReadOnlyList<(string Path, string Method)>> OperationsAsync(TestApi api) =>
    [
        .. (await ContractAsync(api))
            .GetProperty("paths")
            .EnumerateObject()
            .SelectMany(path => path.Value
                .EnumerateObject()
                .Where(operation => Methods.Contains(operation.Name, StringComparer.Ordinal))
                .Select(operation => (path.Name, operation.Name))),
    ];

    private static bool IsUnversioned(string path) =>
        ApiRoutes.UnversionedPaths.Any(allowed =>
            path.Equals(allowed, StringComparison.Ordinal)
            || path.StartsWith(allowed + "/", StringComparison.Ordinal));

    /// <summary>
    /// Fills any <c>{id}</c> template segment with a value that binds, so the walk keeps working
    /// when the first route with a parameter arrives. A 401 must not depend on what the id is.
    /// </summary>
    private static string Concrete(string path) =>
        string.Join('/', path.Split('/').Select(segment =>
            segment.StartsWith('{') ? Guid.Empty.ToString() : segment));
}
