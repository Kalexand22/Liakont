namespace Liakont.Host.FleetApi;

using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.FleetSupervision.Application;
using Liakont.Modules.FleetSupervision.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

/// <summary>
/// Endpoint central de réception des heartbeats de flotte (OPS04). <c>POST /api/fleet/v1/heartbeat</c>,
/// distinct de l'API console OIDC : une instance s'authentifie par CLÉ d'ingestion (en-tête
/// <c>X-Fleet-Key</c>), jamais par OIDC. Actif uniquement quand le rôle CENTRAL est activé
/// (<c>FleetSupervision:Central:Enabled</c>) — sinon 404 (cette instance n'est pas un central).
/// </summary>
internal static class FleetApiEndpoints
{
    public static IEndpointRouteBuilder MapFleetApi(this IEndpointRouteBuilder app)
    {
        app.MapPost(FleetApiHeaders.HeartbeatPath, HandleHeartbeatAsync)
            .AllowAnonymous()
            .DisableAntiforgery();

        return app;
    }

    private static async Task<IResult> HandleHeartbeatAsync(
        HttpContext http,
        IFleetHeartbeatIngestor ingestor,
        IOptions<FleetSupervisionOptions> options,
        CancellationToken ct)
    {
        FleetCentralOptions central = options.Value.Central;

        // Cette instance n'est pas un central : l'endpoint n'existe pas pour elle (pas d'oracle).
        if (!central.Enabled)
        {
            return Results.NotFound();
        }

        // Authentification par clé d'ingestion partagée (comparaison à temps constant).
        string? providedKey = http.Request.Headers[FleetApiHeaders.Key];
        if (!FleetApiKeyValidator.IsAuthorized(central.IngestionKey, providedKey))
        {
            return Results.Unauthorized();
        }

        // Corps validé à la frontière : un corps absent ou sans identifiant d'instance est rejeté en 400.
        InstanceHeartbeatReport? report;
        try
        {
            report = await http.Request.ReadFromJsonAsync<InstanceHeartbeatReport>(FleetTransportJson.Options, ct)
                .ConfigureAwait(false);
        }
        catch (System.Text.Json.JsonException)
        {
            return Results.BadRequest("Corps du heartbeat illisible.");
        }

        if (report is null || string.IsNullOrWhiteSpace(report.InstanceId))
        {
            return Results.BadRequest("Le heartbeat doit porter un identifiant d'instance.");
        }

        await ingestor.RecordAsync(report, ct).ConfigureAwait(false);
        return Results.Accepted();
    }
}
