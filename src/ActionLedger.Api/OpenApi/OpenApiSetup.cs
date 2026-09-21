using System.Text.Json.Nodes;
using ActionLedger.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ActionLedger.Api.OpenApi;

/// <summary>
/// AD-13 — the shape of the committed contract. The document is generated from the endpoints the
/// host maps; everything here is the part that no endpoint can express: the document identity,
/// the bearer scheme, and the paging vocabulary.
/// </summary>
public static class OpenApiSetup
{
    /// <summary>The single document name. It is also the file name segment: <c>/openapi/v1.json</c>.</summary>
    public const string DocumentName = "v1";

    /// <summary>The security scheme id referenced by every authenticated operation.</summary>
    public const string BearerSchemeId = "bearer";

    /// <summary>The component name for the list envelope defined by <see cref="PagedResult{T}"/>.</summary>
    public const string PagedResultComponent = "PagedResult";

    public static IServiceCollection AddApiOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(DocumentName, options =>
        {
            options.AddDocumentTransformer(DescribeContractAsync);
            options.AddOperationTransformer(RequireBearerWhereAuthorizedAsync);
        });

    private static Task DescribeContractAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "ActionLedger API",
            Version = DocumentName,
            Description =
                "Turns meeting notes into tracked, attributable action items. Every route lives under "
                + "/api/v1 except /health, /openapi, and /swagger. Errors are RFC 9457 ProblemDetails "
                + "whose type is one of validation, unauthorized, forbidden, not-found, or conflict.",
        };

        // The server list is derived from whatever request asked for the document, which would make
        // the committed file depend on the port it was generated on. The contract is host-relative.
        document.Servers?.Clear();

        document.Components ??= new OpenApiComponents();

        document.AddComponent(BearerSchemeId, new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description =
                "An HS256 token from POST /api/v1/auth/login carrying sub, name, and role. "
                + "Send it as: Authorization: Bearer {token}.",
        });

        AddPagingVocabulary(document);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Publishes the paging envelope and its two query parameters as reusable components.
    /// </summary>
    /// <remarks>
    /// No operation references them yet — the first list endpoint is Story 1.4's user roster. They
    /// are published now because the generated TypeScript client is built from this file, and the
    /// envelope is a shape the web app should have from its first commit rather than one that
    /// appears mid-epic. <c>OpenApiContractTests</c> asserts the component keeps matching
    /// <see cref="PagedResult{T}"/>, so the two cannot drift apart while unreferenced.
    /// </remarks>
    private static void AddPagingVocabulary(OpenApiDocument document)
    {
        document.AddComponent(PagedResultComponent, new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Title = PagedResultComponent,
            Description = "The envelope every list endpoint returns.",
            Required = new HashSet<string>(StringComparer.Ordinal) { "items", "page", "pageSize", "total" },
            Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
            {
                ["items"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.Array,
                    Items = new OpenApiSchema(),
                    Description = "This page of results. An operation narrows the item type.",
                },
                ["page"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.Integer,
                    Format = "int32",
                    Minimum = Paging.FirstPage.ToString(Culture),
                    Description = "The 1-based page number these items came from.",
                },
                ["pageSize"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.Integer,
                    Format = "int32",
                    Minimum = "1",
                    Maximum = Paging.MaxPageSize.ToString(Culture),
                    Description = "The page size applied, after clamping.",
                },
                ["total"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.Integer,
                    Format = "int32",
                    Minimum = "0",
                    Description = "The number of matching items across every page.",
                },
            },
        });

        document.AddComponent("page", new OpenApiParameter
        {
            Name = "page",
            In = ParameterLocation.Query,
            Required = false,
            Description = "The 1-based page to return.",
            Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.Integer,
                Format = "int32",
                Minimum = Paging.FirstPage.ToString(Culture),
                Default = JsonValue.Create(Paging.FirstPage),
            },
        });

        document.AddComponent("pageSize", new OpenApiParameter
        {
            Name = "pageSize",
            In = ParameterLocation.Query,
            Required = false,
            Description = $"Items per page. Values above {Paging.MaxPageSize} are clamped to {Paging.MaxPageSize}.",
            Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.Integer,
                Format = "int32",
                Minimum = "1",
                Maximum = Paging.MaxPageSize.ToString(Culture),
                Default = JsonValue.Create(Paging.DefaultPageSize),
            },
        });
    }

    /// <summary>
    /// Attaches the bearer requirement to every operation that carries an authorization policy,
    /// so Swagger UI's Authorize button and the generated client both know which calls need a token.
    /// </summary>
    private static Task RequireBearerWhereAuthorizedAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        IList<object> metadata = context.Description.ActionDescriptor.EndpointMetadata;

        bool anonymous = metadata.OfType<IAllowAnonymous>().Any();
        bool authorized = metadata.OfType<IAuthorizeData>().Any();

        if (anonymous || !authorized)
        {
            return Task.CompletedTask;
        }

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(BearerSchemeId, context.Document)] = [],
        });

        return Task.CompletedTask;
    }

    private static readonly System.Globalization.CultureInfo Culture =
        System.Globalization.CultureInfo.InvariantCulture;
}
