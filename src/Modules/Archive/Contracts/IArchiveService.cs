namespace Liakont.Modules.Archive.Contracts;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Surface publique du module Archive (TRK05). Crée le paquet d'archive WORM d'un document émis, ajoute
/// des addenda chaînés, et vérifie l'intégrité de la chaîne du tenant. Tenant-scopé par construction : la
/// persistance route vers la base du tenant courant et le coffre vers la racine du tenant courant
/// (blueprint §7). Consommé par le pipeline (PIP) à l'émission d'un document, par la récupération du
/// tax-report et par la réconciliation PDF (addenda), et par l'export/vérification (TRK06).
/// </summary>
public interface IArchiveService
{
    /// <summary>
    /// Archive un document ÉMIS : compose le paquet (payload, réponse PA, rendu lisible, pièces présentes
    /// avec motif d'absence sinon, manifest), l'écrit dans le coffre (write-once) et scelle l'entrée en
    /// base en l'ajoutant à la chaîne de hashes du tenant.
    /// </summary>
    Task<ArchivePackageResult> ArchiveIssuedDocumentAsync(ArchivePackageRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ajoute un addendum CHAÎNÉ à un paquet existant (tax-report récupéré, PDF réconcilié) : nouveau
    /// fichier + manifest-addendum, jamais une réécriture du paquet (WORM).
    /// </summary>
    Task<ArchivePackageResult> AddAddendumAsync(ArchiveAddendumRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Vérifie l'intégrité de TOUTE la chaîne d'archive du tenant courant (contenu + chaînage) et produit
    /// un rapport. Base de l'export contrôle fiscal et de la vérification à la demande (TRK06).
    /// </summary>
    Task<ArchiveIntegrityReport> VerifyTenantChainAsync(CancellationToken cancellationToken = default);
}
