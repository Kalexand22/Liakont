namespace Liakont.Modules.Supervision.Application.Rules;

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Supervision.Domain;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;

/// <summary>
/// Base des règles « documents non traités depuis &gt; N jours » (F12 §5.2) : un document qui stagne dans un
/// état d'attente (Blocked, RejectedByPa) au-delà du seuil du tenant (défaut produit, surchargé via CFG02).
/// La source de données est le module Documents par son Contract (<see cref="IDocumentQueries"/>) — l'âge se
/// dérive de <c>LastUpdateUtc</c> (temps passé dans l'état) du document le PLUS ANCIEN de l'état, lu sans
/// paginer la file. Aucune donnée fabriquée. La règle est PURE : l'anti-bruit / l'auto-résolution sont
/// portés par le moteur SUP01a.
/// </summary>
public abstract class DocumentStateAgeAlertRule : IAlertRule
{
    private readonly IDocumentQueries _documents;
    private readonly ITenantSettingsQueries _tenantSettings;

    protected DocumentStateAgeAlertRule(IDocumentQueries documents, ITenantSettingsQueries tenantSettings)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(tenantSettings);

        _documents = documents;
        _tenantSettings = tenantSettings;
    }

    public abstract string RuleKey { get; }

    public abstract AlertSeverity Severity { get; }

    /// <summary>État surveillé — valeur TEXTE persistée du document (F06 §3, <c>DocumentState</c>).</summary>
    protected abstract string State { get; }

    /// <summary>Seuil par défaut produit (jours, F12 §5.2), appliqué quand le tenant n'a pas de seuils (CFG02).</summary>
    protected abstract int DefaultThresholdDays { get; }

    public async Task<AlertEvaluation> EvaluateAsync(AlertEvaluationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var oldest = await _documents.GetOldestDocumentInStateAsync(State, cancellationToken).ConfigureAwait(false);
        if (oldest is null)
        {
            return AlertEvaluation.Clear();
        }

        var thresholdDays = await ResolveThresholdDaysAsync(cancellationToken).ConfigureAwait(false);

        // Seuil « > N jours » strict (F12 §5.2) : un document pile à N jours n'a pas encore dépassé le seuil.
        if (context.NowUtc - oldest.LastUpdateUtc <= TimeSpan.FromDays(thresholdDays))
        {
            return AlertEvaluation.Clear();
        }

        return AlertEvaluation.Firing(BuildDetail(oldest, thresholdDays));
    }

    /// <summary>Horodatage UTC lisible pour les messages opérateur.</summary>
    protected static string FormatUtc(DateTimeOffset value) =>
        value.UtcDateTime.ToString("dd/MM/yyyy HH:mm 'UTC'", CultureInfo.InvariantCulture);

    /// <summary>Seuil du tenant (jours) pour cet état, extrait de ses seuils d'alerte (CFG02).</summary>
    protected abstract int TenantThresholdDays(AlertThresholdsDto thresholds);

    /// <summary>Message opérateur (français, n° de document + action corrective) quand la condition est remplie.</summary>
    protected abstract string BuildDetail(DocumentSummaryDto oldest, int thresholdDays);

    private async Task<int> ResolveThresholdDaysAsync(CancellationToken cancellationToken)
    {
        var companyId = await _tenantSettings.GetCurrentCompanyId(cancellationToken).ConfigureAwait(false);
        if (companyId is not { } id)
        {
            return DefaultThresholdDays;
        }

        var thresholds = await _tenantSettings.GetAlertThresholds(id, cancellationToken).ConfigureAwait(false);
        return thresholds is null ? DefaultThresholdDays : TenantThresholdDays(thresholds);
    }
}
