using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace ActionLedger.Api.Routing;

/// <summary>
/// AD-13 — every controller route lives under <c>/api/v1</c>. Applying it as a convention rather
/// than as a literal in each <c>[Route]</c> means a new controller cannot forget the prefix, and
/// <c>RouteDisciplineTests</c> proves the rule against the endpoints the host actually maps.
/// </summary>
public static class ApiRoutes
{
    /// <summary>The prefix every controller route carries.</summary>
    public const string Prefix = "api/v1";

    /// <summary>The only paths allowed to sit outside the prefix.</summary>
    public static readonly string[] UnversionedPaths = ["/health", "/openapi", "/swagger"];
}

/// <summary>Prepends <see cref="ApiRoutes.Prefix"/> to every controller's route.</summary>
internal sealed class ApiRoutePrefixConvention : IApplicationModelConvention
{
    private static readonly AttributeRouteModel Prefix = new(new RouteAttribute(ApiRoutes.Prefix));

    public void Apply(ApplicationModel application)
    {
        foreach (ControllerModel controller in application.Controllers)
        {
            foreach (SelectorModel selector in controller.Selectors)
            {
                selector.AttributeRouteModel = selector.AttributeRouteModel is null
                    ? Prefix
                    : AttributeRouteModel.CombineAttributeRouteModel(Prefix, selector.AttributeRouteModel);
            }
        }
    }
}
