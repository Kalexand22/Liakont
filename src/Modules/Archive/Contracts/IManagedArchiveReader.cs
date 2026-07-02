namespace Liakont.Modules.Archive.Contracts;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Lecture d'un paquet GED déjà rangé write-once dans le coffre du tenant (F19 §5.1/§6.7, option C) : surface
/// de CONSULTATION, sœur en lecture de <see cref="IGenericArchiveService"/> (écriture). Tenant-scopée (le coffre
/// est rooté sur le tenant courant, blueprint §7). Portée aux paquets GED (<c>_ged/…</c>) : l'intégrité FISCALE
/// (chaîne + ancrage) reste celle d'<see cref="IArchiveVerifier"/>, réservée à un document fiscal.
///
/// Cette surface encapsule la connaissance du format de paquet (manifest, empreintes de pièces, empreinte de
/// paquet) DANS le module Archive : les consommateurs (page fiche GED09b, exports) n'ont pas à re-parser le
/// coffre ni à réimplémenter le calcul d'empreinte (frontière : Ged/Host → Archive.Contracts uniquement).
/// </summary>
public interface IManagedArchiveReader
{
    /// <summary>
    /// Vérifie l'intégrité d'un paquet GED : RE-LIT les octets réels du coffre (manifest + pièces), recalcule
    /// leur empreinte de paquet et la compare à <paramref name="indexedContentHash"/> (§3.4.1). Ne fait JAMAIS
    /// confiance à une valeur en base seule. Rend <see cref="GedArchiveIntegrityStatus.NotArchived"/> si
    /// <paramref name="manifestPath"/> ou <paramref name="indexedContentHash"/> est absent.
    /// </summary>
    Task<GedArchiveIntegrityResult> VerifyManagedPackageAsync(
        string? manifestPath,
        string? indexedContentHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lit le rendu lisible autonome OPTIONNEL du paquet (<c>document-lisible.html</c>, aperçu console), ou
    /// <see langword="null"/> s'il est absent. C'est le <c>ReadableHtml</c> fourni au rangement (RL-16) —
    /// JAMAIS le moteur facture <c>ReadableDocumentRenderer</c>, réservé aux documents fiscaux.
    /// </summary>
    Task<string?> ReadManagedReadableHtmlAsync(
        string? manifestPath,
        CancellationToken cancellationToken = default);
}
