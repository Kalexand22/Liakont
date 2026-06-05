namespace Liakont.Modules.Staging.Contracts;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Sonde de présence EFFECTIVE du paquet d'archive WORM d'un document émis (ADR-0014 §4). Port d'inversion
/// de dépendance OWNED par le module Staging : il exprime le besoin « le contenu est-il PROUVÉ préservé
/// dans le coffre ? » sans coupler le staging au module Archive. L'implémentation (qui interroge
/// <c>IArchiveStore.ExistsAsync</c>) est câblée au composition root (Host), seul endroit autorisé à
/// référencer le coffre concret — la frontière inter-modules (Contracts uniquement) reste intacte.
///
/// La présence du <b>blob WORM</b> (pas la seule étiquette d'état <c>Issued</c>) est la condition de purge :
/// la transition Issued puis l'écriture WORM (TRK05) ne sont pas atomiques.
/// </summary>
public interface IArchivedDocumentProbe
{
    /// <summary>
    /// Indique si le paquet d'archive WORM du document localisé est EFFECTIVEMENT présent dans le coffre du
    /// tenant courant (tenant-scopé). <c>false</c> tant que l'écriture WORM n'est pas confirmée — la purge
    /// du staging reste alors suspendue (le contenu n'est pas encore prouvé préservé).
    /// </summary>
    /// <param name="locator">La localisation du paquet (année/mois d'émission + numéro).</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    Task<bool> IsArchivedAsync(ArchivedDocumentLocator locator, CancellationToken cancellationToken = default);
}
