namespace Liakont.Modules.Supervision.Infrastructure;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Supervision.Application;
using Liakont.Modules.Supervision.Contracts;
using Liakont.Modules.Supervision.Contracts.DTOs;
using Liakont.Modules.Supervision.Domain;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Microsoft.Extensions.Options;

/// <summary>
/// Lecture du dispositif d'alerte (FIX210, F12 §5). Croise le catalogue déclaratif (<see cref="AlertRuleCatalog"/>,
/// F12 §5.2) avec les règles RÉELLEMENT enregistrées (<see cref="IAlertRule"/>, SUP01b) pour distinguer actif/gelé,
/// et restitue le seuil EFFECTIF du tenant (seuils CFG02 lus par le Contract TenantSettings, repli sur les défauts
/// F12 §5.2). L'état de l'e-mail opérateur vient des options d'instance (F12 §5.3) — l'adresse n'est jamais exposée.
/// Aucune logique fiscale ni règle inventée : tout vient du catalogue F12 §5.2.
/// </summary>
public sealed class AlertDeviceQueries : IAlertDeviceQueries
{
    /// <summary>Cadence du dead-man's-switch (F12 §5.1 : toutes les 15 min) — restituée à l'opérateur.</summary>
    public const int EvaluationIntervalMinutes = 15;

    private readonly IReadOnlyCollection<string> _activeRuleKeys;
    private readonly ITenantSettingsQueries _tenantSettings;
    private readonly SupervisionNotificationOptions _notificationOptions;

    public AlertDeviceQueries(
        IEnumerable<IAlertRule> rules,
        ITenantSettingsQueries tenantSettings,
        IOptions<SupervisionNotificationOptions> notificationOptions)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(tenantSettings);
        ArgumentNullException.ThrowIfNull(notificationOptions);

        // L'état actif se dérive des règles enregistrées (une règle SUP01c devenue implémentée passe « active »
        // sans toucher cette lecture). Comparaison sur la clé stable, sensible à la casse.
        _activeRuleKeys = rules.Select(static r => r.RuleKey).ToHashSet(StringComparer.Ordinal);
        _tenantSettings = tenantSettings;
        _notificationOptions = notificationOptions.Value;
    }

    public async Task<AlertDeviceStatusDto> GetDeviceStatusAsync(CancellationToken cancellationToken = default)
    {
        var thresholds = await ResolveThresholdsAsync(cancellationToken).ConfigureAwait(false);

        var rules = AlertRuleCatalog.All
            .Select(descriptor => new AlertRuleStatusDto
            {
                RuleKey = descriptor.RuleKey,
                DisplayName = descriptor.DisplayName,
                Severity = SeverityLabel(descriptor.Severity),
                IsActive = _activeRuleKeys.Contains(descriptor.RuleKey),
                ThresholdDisplay = ThresholdDisplay(descriptor.ThresholdKind, thresholds),
            })
            .ToList();

        return new AlertDeviceStatusDto
        {
            Rules = rules,
            OperatorEmailConfigured = !string.IsNullOrWhiteSpace(_notificationOptions.OperatorEmail),
            EvaluationIntervalMinutes = EvaluationIntervalMinutes,
        };
    }

    private static string SeverityLabel(AlertSeverity severity) =>
        severity == AlertSeverity.Critical ? "Critique" : "Avertissement";

    private static string ThresholdDisplay(AlertRuleThresholdKind kind, AlertThresholdsDto? t) => kind switch
    {
        AlertRuleThresholdKind.AgentSilentHours =>
            FormatHours(t?.AgentSilentHours ?? AlertRuleCatalog.DefaultAgentSilentHours),
        AlertRuleThresholdKind.MissedRunHours =>
            FormatHours(t?.MissedRunHours ?? AlertRuleCatalog.DefaultMissedRunHours),
        AlertRuleThresholdKind.PushQueue => string.Format(
            CultureInfo.InvariantCulture,
            "> {0} éléments ou > {1} h",
            t?.PushQueueMaxItems ?? AlertRuleCatalog.DefaultPushQueueMaxItems,
            t?.PushQueueMaxAgeHours ?? AlertRuleCatalog.DefaultPushQueueMaxAgeHours),
        AlertRuleThresholdKind.BlockedDocumentsDays =>
            FormatDays(t?.BlockedDocumentsDays ?? AlertRuleCatalog.DefaultBlockedDocumentsDays),
        AlertRuleThresholdKind.PaRejectionsDays =>
            FormatDays(t?.PaRejectionsDays ?? AlertRuleCatalog.DefaultPaRejectionsDays),
        AlertRuleThresholdKind.DeadlineFixed => "J-3",
        _ => "—",
    };

    private static string FormatHours(int hours) =>
        string.Format(CultureInfo.InvariantCulture, "> {0} h", hours);

    private static string FormatDays(int days) =>
        string.Format(CultureInfo.InvariantCulture, "> {0} j", days);

    /// <summary>Seuils du tenant courant (CFG02) ; <c>null</c> si non encore définis (repli sur les défauts F12 §5.2).</summary>
    private async Task<AlertThresholdsDto?> ResolveThresholdsAsync(CancellationToken cancellationToken)
    {
        var companyId = await _tenantSettings.GetCurrentCompanyId(cancellationToken).ConfigureAwait(false);
        if (companyId is not { } id)
        {
            return null;
        }

        return await _tenantSettings.GetAlertThresholds(id, cancellationToken).ConfigureAwait(false);
    }
}
