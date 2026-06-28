namespace Liakont.Host.B2cReporting;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Façade Host (composition EN LECTURE) de la page des émissions e-reporting B2C de la marge (B4). Isole la
/// page de la couche Query du module Pipeline et projette le DTO en modèle de présentation. Tenant-scopée par
/// construction (la couche Query lit la base du tenant courant — CLAUDE.md n°9/17).
/// </summary>
internal interface IB2cMarginEmissionsConsoleQueries
{
    /// <summary>
    /// Agrégats d'émission de la marge du tenant courant, optionnellement bornés à une période année-mois
    /// (<c>"yyyy-MM"</c>) sur le jour de l'agrégat — un filtre de DATE pur (jamais une règle fiscale).
    /// </summary>
    Task<B2cMarginEmissionsViewModel> GetEmissionsAsync(string? period, CancellationToken cancellationToken = default);

    /// <summary>
    /// Détail d'UNE transmission (BUG-22) : état courant + motif PA lisible + pièces composant l'agrégat.
    /// <c>null</c> si le lot d'émission est introuvable pour le tenant courant.
    /// </summary>
    Task<B2cMarginEmissionDetailViewModel?> GetEmissionDetailAsync(Guid emissionBatchId, CancellationToken cancellationToken = default);
}
