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

    /// <summary>
    /// Dossier d'export pour une PLAGE de dates [<paramref name="fromInclusive"/>, <paramref name="toInclusive"/>]
    /// (bornes incluses). Forme requêtée par l'endpoint console <c>GET /api/v1/audit-export?from=&amp;to=</c> (API03).
    /// <para>
    /// ATTENTION — granularité MENSUELLE (à la différence de <c>GET /api/v1/documents</c>, qui filtre au JOUR
    /// près) : le coffre étant partitionné par <c>&lt;année&gt;/&lt;mois&gt;/</c>, un paquet est retenu dès que le MOIS de
    /// son chemin de coffre est compris entre le mois de <paramref name="fromInclusive"/> et celui de
    /// <paramref name="toInclusive"/> — le jour est ignoré (une borne en milieu de mois retient tout le mois).
    /// </para>
    /// Une borne <c>null</c> = pas de limite de ce côté ; les deux <c>null</c> = TOUT le coffre du tenant
    /// (réservé à l'export de réversibilité ; l'endpoint de lecture exige au moins une borne).
    /// </summary>
    Task<FiscalControlExport> BuildForRangeAsync(DateOnly? fromInclusive, DateOnly? toInclusive, CancellationToken cancellationToken = default);
}
