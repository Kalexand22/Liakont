namespace Liakont.Modules.Pipeline.Application;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Journal APPEND-ONLY des rectificatifs d'e-reporting (<c>pipeline.report_rectifications</c>, PIP04, flux RE)
/// sur la base DU TENANT courant (la connexion EST le tenant — database-per-tenant, blueprint §7 ; aucun
/// accès cross-tenant). Conserve l'historique COMPLET (déclaration initiale + chaque rectificatif), jamais
/// modifié ni effacé (garanti par triggers base — CLAUDE.md n°4). Sert l'idempotence (comparaison d'empreinte)
/// et la ré-évaluation des périodes déjà déclarées.
/// </summary>
public interface IReportRectificationLedger
{
    /// <summary>
    /// Dernière entrée (tous statuts) pour la clé de période, ou <c>null</c> si la période n'a jamais été
    /// déclarée. Sert la décision d'idempotence (comparaison d'empreinte + statut).
    /// </summary>
    Task<ReportRectificationEntry?> GetLatestAsync(
        PaymentReportFlux flux,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken cancellationToken = default);

    /// <summary>Ajoute une entrée au journal (append-only — aucun update/delete possible, garanti en base).</summary>
    Task AppendAsync(ReportRectificationEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Périodes DISTINCTES déjà déclarées au journal (clé flux + bornes) — consommé par la ré-évaluation
    /// (un avoir / une altération source modifie l'agrégat ; le job émet alors le rectificatif RE).
    /// </summary>
    Task<IReadOnlyList<RectificationPeriodKey>> ListDeclaredPeriodsAsync(CancellationToken cancellationToken = default);

    /// <summary>Historique chronologique complet d'une période (initiale + rectificatifs) — console / tests.</summary>
    Task<IReadOnlyList<ReportRectificationEntry>> ListByPeriodAsync(
        PaymentReportFlux flux,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken cancellationToken = default);
}
