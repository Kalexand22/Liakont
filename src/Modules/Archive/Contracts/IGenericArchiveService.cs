namespace Liakont.Modules.Archive.Contracts;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Surface d'archivage GÉNÉRIQUE (F19 §5.1, option C, RL-05) : range un document arbitraire (NON fiscal) dans
/// le coffre WORM du tenant, write-once, sous <c>_ged/{kind}/{année}/{mois}/{clé}/</c>, HORS de la chaîne de
/// hashes fiscale (<c>documents.archive_entries</c>). Additive et hash-neutre pour la facture :
/// <see cref="IArchiveService.ArchiveIssuedDocumentAsync"/> reste la SEULE voie fiscale (inchangée). La
/// hash-neutralité est STRUCTURELLE (INV-ARCH-GED-1) : cette surface ne touche jamais la chaîne fiscale ni
/// <c>documents.archive_entries</c>. Tenant-scopé (le coffre est rooté sur le tenant courant, blueprint §7).
///
/// Le port de coffre TIERS probant (<c>ISealedArchiveProvider</c>) et sa table <c>sealed_refs</c> ne sont PAS
/// posés ici : ils arrivent avec le premier provider (fast-follow GED20, RL-26).
/// </summary>
public interface IGenericArchiveService
{
    /// <summary>
    /// Range un document GED write-once dans le coffre du tenant. Idempotent pour un paquet identique
    /// (re-rangement = no-op renvoyant <see cref="GedArchivePackageResult.AlreadyArchived"/> = true).
    /// </summary>
    Task<GedArchivePackageResult> ArchiveManagedDocumentAsync(
        GedArchivePackageRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ajoute un addendum write-once à un paquet GED existant. Idempotent par empreinte de contenu (le nom de
    /// stockage est dérivé du hash, pas de sondage).
    /// </summary>
    Task<GedArchivePackageResult> AddManagedAddendumAsync(
        GedArchiveAddendumRequest request,
        CancellationToken cancellationToken = default);
}
