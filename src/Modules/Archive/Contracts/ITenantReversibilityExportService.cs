namespace Liakont.Modules.Archive.Contracts;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Construit le dossier de RÉVERSIBILITÉ COMPLET du tenant courant (F12 §6.3 ; <c>blueprint.md</c> §6 :
/// l'export/réversibilité est une responsabilité du module Archive). Tenant-scopé par construction. Consommé
/// par API03 (<c>GET /api/v1/tenant-export</c>). Agrège, sans jamais révéler de secret (clés API des PA
/// masquées — INV-TENANTSETTINGS-003) : le suivi des documents, le coffre d'archive, le paramétrage et le
/// journal d'audit du tenant.
/// </summary>
public interface ITenantReversibilityExportService
{
    /// <summary>Assemble le dossier de réversibilité du tenant courant.</summary>
    Task<TenantReversibilityExport> BuildAsync(CancellationToken cancellationToken = default);
}
