namespace Liakont.Modules.Ingestion.Application;

using System.IO;

/// <summary>
/// Stockage FICHIER des PDF reçus de l'agent, organisé par tenant (F12 §3.2, PIV04 ; organisation
/// documentée par <c>docs/adr/ADR-0008-stockage-pdf-ingestion.md</c>). Abstraction délibérée : le
/// module <c>Document</c> du socle Stratum n'est PAS vendoré (ce n'est pas une option) ; l'ingestion
/// ne dépend que de ce port. La V1 stocke sur système de fichiers ; un backend objet (S3-compatible)
/// se branche derrière la même abstraction sans toucher aux appelants.
/// </summary>
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
