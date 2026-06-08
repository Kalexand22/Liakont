namespace Liakont.Modules.TenantSettings.Web;

using System.Threading;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

/// <summary>
/// Endpoint de lecture du paramétrage du tenant pour la console (API01c), monté sous <c>/api/v1/settings</c>
/// par le Host. La lecture est TENANT-SCOPÉE par construction (la connexion EST le tenant — database-per-tenant,
/// blueprint §7 ; CLAUDE.md n°9/17) et exige la permission <c>liakont.read</c>. Aucune logique métier ici :
/// l'endpoint délègue la composition (profil + fiscal + comptes PA masqués + état TVA + capacités PA) au
/// service <see cref="ITenantSettingsConsoleQueries"/> du module. Les secrets ne sont JAMAIS exposés
/// (INV-TENANTSETTINGS-003) et aucune capacité agent/adaptateur n'est servie ici (reportée à API01d).
/// </summary>
public static class TenantSettingsEndpointMapping
{
    /// <summary>
    /// Permission de consultation (canonique : <c>LiakontPermissions.Read</c> dans le Host, cataloguée par
    /// Identity). Référencée en chaîne car un projet de module ne référence pas le Host (frontière de dépendance).
    /// </summary>
    private const string ReadPermission = "liakont.read";

    public static IEndpointRouteBuilder MapTenantSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /api/v1/settings — paramétrage du tenant courant (secrets masqués, état TVA, capacités PA).
        app.MapGet("/settings", async (
            ITenantSettingsConsoleQueries queries,
            CancellationToken ct) =>
        {
            var overview = await queries.GetSettingsOverview(ct);
            return Results.Ok(overview);
        }).RequireAuthorization(ReadPermission);

        return app;
    }
}
