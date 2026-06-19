namespace Liakont.Modules.Mandats.Web;

using Microsoft.AspNetCore.Routing;

/// <summary>
/// Point de montage des endpoints console du module Mandats. MND01 (fondation) ne monte AUCUNE route :
/// les écrans du lot (registre des mandants, édition/validation/révocation des mandats) sont livrés par
/// les items MND ultérieurs, qui ajouteront leurs routes ici (sous <c>/api/v1/settings/mandats</c>,
/// gardées par les permissions <c>liakont.read</c>/<c>liakont.settings</c>), sans toucher la composition
/// root. Le squelette est livré dès la fondation pour rester homogène avec les autres modules.
/// </summary>
public static class MandatsEndpointMapping
{
    /// <summary>
    /// Monte les endpoints du module Mandats. Aucune route à ce stade (MND01) — voir la remarque de classe.
    /// </summary>
    public static IEndpointRouteBuilder MapMandatsEndpoints(this IEndpointRouteBuilder app)
    {
        return app;
    }
}
