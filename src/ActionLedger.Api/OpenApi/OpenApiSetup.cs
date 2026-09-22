using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using ActionLedger.Application.Abstractions;
using ActionLedger.Application.Meetings;
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

    /// <summary>The reusable query parameters every list operation takes.</summary>
    public static readonly string[] PagingParameterComponents = ["page", "pageSize"];

    public static IServiceCollection AddApiOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(DocumentName, options =>
        {
            options.AddDocumentTransformer(DescribeContractAsync);
            options.AddSchemaTransformer(DescribeEnumsAsStringsAsync);
            options.AddSchemaTransformer(DescribeCollectionElementBoundsAsync);
            options.AddSchemaTransformer(DescribeRequiredMembersAsNonNullableAsync);
            options.AddOperationTransformer(RequireBearerWhereAuthorizedAsync);
            options.AddOperationTransformer(ReusePagingVocabularyAsync);
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
    /// <para>
    /// Story 1.2 published them unreferenced, for the first list endpoint to pick up. That is
    /// Story 1.4's user roster: <see cref="ReusePagingVocabularyAsync"/> now points the roster's
    /// <c>page</c> and <c>pageSize</c> query parameters at these definitions, so the bounds and
    /// defaults a list operation documents are the ones <see cref="Paging"/> actually applies
    /// rather than a copy per operation.
    /// </para>
    /// <para>
    /// The envelope schema stays a reference shape rather than something an operation
    /// <c>$ref</c>s. A list operation's response is generated as a concrete
    /// <c>PagedResultOfUserSummaryDto</c>, which is what gives the generated client a typed
    /// <c>items</c>; referencing the untyped envelope instead would throw that away.
    /// <c>OpenApiContractTests</c> asserts the component keeps matching
    /// <see cref="PagedResult{T}"/>, so the two cannot drift apart.
    /// </para>
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

    /// <summary>
    /// Publishes every enum as the PascalCase string it is actually serialized as.
    /// </summary>
    /// <remarks>
    /// The generator does not read the MVC serializer's settings, so an enum that
    /// <c>AddJsonOptions</c>' <c>JsonStringEnumConverter</c> writes as <c>"ActionOfficer"</c> is
    /// otherwise published as a bare <c>integer</c> with no values. AD-13 makes this file the
    /// boundary Story 1.5's client is generated from, so that mismatch would type <c>role</c> as
    /// a number and break on every response that carries one.
    ///
    /// The names come from the type itself rather than from a list kept here, so adding a Role or
    /// renaming one updates the contract by re-exporting and nothing else.
    /// </remarks>
    private static Task DescribeEnumsAsStringsAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        Type type = Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) ?? context.JsonTypeInfo.Type;

        if (!type.IsEnum)
        {
            return Task.CompletedTask;
        }

        schema.Type = JsonSchemaType.String;
        schema.Enum = [.. Enum.GetNames(type).Select(name => (JsonNode)JsonValue.Create(name)!)];

        return Task.CompletedTask;
    }

    /// <summary>
    /// Publishes the per-element length rule a collection property's validation attribute
    /// enforces, on the <c>items</c> schema of that collection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The generator reads <c>[Required]</c>, <c>[StringLength]</c>, and <c>[Range]</c> off a
    /// property and stamps them on that property's own schema — which is why <c>title</c> and the
    /// notes <c>text</c> both publish their bounds. <c>[StringLength]</c> cannot express a
    /// per-element rule at all (its <c>IsValid</c> casts to <c>string</c> and throws on an array),
    /// so the rule lives in <see cref="AttendeeNamesAttribute"/> instead and nothing published it:
    /// <c>attendees</c> went out as a bare array of strings, leaving the generated client and any
    /// integrator to discover the limit from a 400.
    /// </para>
    /// <para>
    /// This keys on <see cref="AttendeeNamesAttribute"/> specifically, so it publishes exactly the
    /// one collection that has such a rule today. A second bounded collection needs either its own
    /// case here or a shared base attribute to match on — this is not yet general.
    /// </para>
    /// </remarks>
    private static Task DescribeCollectionElementBoundsAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        AttendeeNamesAttribute? bounds = context.JsonPropertyInfo?.AttributeProvider?
            .GetCustomAttributes(typeof(AttendeeNamesAttribute), inherit: true)
            .OfType<AttendeeNamesAttribute>()
            .FirstOrDefault();

        if (bounds is not null && schema.Items is OpenApiSchema items)
        {
            items.MinLength = bounds.MinimumLength;
            items.MaxLength = bounds.MaximumLength;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Drops <c>null</c> from the published type of a member the server marks <c>[Required]</c>.
    /// </summary>
    /// <remarks>
    /// The generator derives a member's schema type from its CLR type, so a nullable value type
    /// publishes as <c>["null", <em>t</em>]</c> even when <c>[Required]</c> refuses null. That is
    /// how <c>CreateMeetingCommand.MeetingDate</c> came out: <c>DateOnly?</c> so that an omitted
    /// date is a 400 rather than <c>0001-01-01</c>, and therefore published as nullable while
    /// sitting in the schema's <c>required</c> list. A client that took the contract at its word
    /// and sent <c>"meetingDate": null</c> was answered with a 400 the schema did not predict.
    /// Removing the null bit leaves the published type saying what the server actually accepts.
    /// </remarks>
    private static Task DescribeRequiredMembersAsNonNullableAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        bool required = context.JsonPropertyInfo?.AttributeProvider?
            .GetCustomAttributes(typeof(RequiredAttribute), inherit: true)
            .Length > 0;

        if (required && schema.Type is { } type && type.HasFlag(JsonSchemaType.Null))
        {
            schema.Type = type & ~JsonSchemaType.Null;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Replaces a list operation's generated <c>page</c> and <c>pageSize</c> query parameters
    /// with references to the published components, so the paging vocabulary is defined once.
    /// </summary>
    private static Task ReusePagingVocabularyAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (operation.Parameters is null)
        {
            return Task.CompletedTask;
        }

        for (int index = 0; index < operation.Parameters.Count; index++)
        {
            IOpenApiParameter parameter = operation.Parameters[index];

            if (parameter.In == ParameterLocation.Query
                && parameter.Name is { } name
                && PagingParameterComponents.Contains(name, StringComparer.Ordinal))
            {
                operation.Parameters[index] = new OpenApiParameterReference(name, context.Document);
            }
        }

        return Task.CompletedTask;
    }

    private static readonly System.Globalization.CultureInfo Culture =
        System.Globalization.CultureInfo.InvariantCulture;
}
