namespace Liakont.Modules.Ingestion.Infrastructure;

using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Résout le tenant courant requis par une opération de GESTION d'agents (enregistrement, révocation,
/// rotation, liste). Une opération de gestion sans tenant résolu échoue plutôt que de risquer une
/// fuite cross-tenant (CLAUDE.md n°9). Ne s'applique pas au chemin d'authentification, qui précède
/// tout contexte tenant.
/// </summary>
internal static class IngestionTenantScope
{
    public static string Require(ITenantContext tenantContext)
    {
        var tenantId = tenantContext.TenantId;
        if (string.IsNullOrEmpty(tenantId))
        {
            throw new InvalidOperationException(
                "Aucun tenant résolu : la gestion des agents doit s'effectuer dans le contexte d'un tenant.");
        }

        return tenantId;
    }
}
