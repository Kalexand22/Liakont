namespace Liakont.Modules.Archive.Contracts;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Construit le dossier d'export contrôle fiscal (TRK06, F06 §7) pour un document ou une période.
/// Tenant-scopé par construction. Consommé par API03 (endpoint d'export + endpoint de vérification à la
/// demande). Le dossier réunit paquets d'archive, rapport d'intégrité, preuves d'ancrage, chronologie
/// (DocumentEvents) et notice de vérification en français.
/// </summary>
public interface IFiscalControlExportService
{
    /// <summary>Dossier d'export pour UN document du tenant (par identifiant).</summary>
    Task<FiscalControlExport> BuildForDocumentAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dossier d'export pour une PÉRIODE : une année et, optionnellement, un mois (1-12). Sélectionne les
    /// paquets dont le chemin de coffre relève de la période, puis assemble le dossier de chaque document.
    /// </summary>
    Task<FiscalControlExport> BuildForPeriodAsync(int year, int? month, CancellationToken cancellationToken = default);
}
