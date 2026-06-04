namespace Liakont.Modules.Ingestion.Contracts;

using System.IO;

/// <summary>
/// Stockage FICHIER des PDF reçus de l'agent, organisé par tenant (F12 §3.2, PIV04 ; organisation
/// documentée par <c>docs/adr/ADR-0008-stockage-pdf-ingestion.md</c>). Abstraction délibérée : le
/// module <c>Document</c> du socle Stratum n'est PAS vendoré (ce n'est pas une option) ; l'ingestion
/// ne dépend que de ce port. La V1 stocke sur système de fichiers ; un backend objet (S3-compatible)
/// se branche derrière la même abstraction sans toucher aux appelants.
/// </summary>
/// <remarks>
/// Port exposé sur la surface <c>Contracts</c> (et non via une commande MediatR) car les endpoints PDF
/// STREAMENT le corps de la requête directement vers le stockage : modéliser un <see cref="Stream"/>
/// comme une commande DTO le ferait transiter par les pipeline behaviors orientés données — un
/// anti-pattern pour du binaire volumineux. Le Host (racine de composition) consomme donc ce port
/// directement, exactement comme il consomme <c>IAgentAuthenticator</c> (PIV05) dans son filtre
/// d'authentification : deux services <c>Contracts</c> non-MediatR câblés par la composition root.
/// </remarks>
public interface IIngestedPdfStore
{
    /// <summary>
    /// Stocke un PDF RATTACHÉ à un document (par sa référence source), pour le tenant donné. Renvoie
    /// le chemin de stockage relatif (audit). Un re-push du même document écrase l'entrée précédente.
    /// </summary>
    Task<string> SaveLinkedPdfAsync(string tenantId, string sourceReference, Stream content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stocke un PDF NON RATTACHÉ dans le POOL de réconciliation du tenant (F06/TRK07). Renvoie le
    /// chemin de stockage relatif. Chaque dépôt est conservé distinctement (pas d'écrasement).
    /// </summary>
    Task<string> SavePooledPdfAsync(string tenantId, string fileName, Stream content, CancellationToken cancellationToken = default);
}
