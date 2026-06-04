namespace Liakont.Modules.Archive.Contracts;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Vérifieur d'intégrité COMPLET du coffre d'un tenant (TRK06) : recalcule contenu + chaînage de TOUTES
/// les entrées (paquets et addenda) ET vérifie les preuves d'ancrage temporel. Tenant-scopé par
/// construction (coffre + base routés vers le tenant courant). Déclenchable À LA DEMANDE par l'opérateur
/// (API03/WEB04) et inclus dans l'export contrôle fiscal — l'export reste vérifiable sans attendre un contrôle.
/// </summary>
public interface IArchiveVerifier
{
    /// <summary>Vérifie l'intégrité de TOUT le coffre du tenant courant (chaîne + ancrages) et produit le rapport.</summary>
    Task<ArchiveVerificationReport> VerifyTenantVaultAsync(CancellationToken cancellationToken = default);
}
