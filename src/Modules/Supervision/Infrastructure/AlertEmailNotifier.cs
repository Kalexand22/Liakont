namespace Liakont.Modules.Supervision.Infrastructure;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Supervision.Application;
using Liakont.Modules.Supervision.Contracts;
using Liakont.Modules.Supervision.Contracts.DTOs;
using Liakont.Modules.Supervision.Domain;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stratum.Modules.Job.Contracts;
using Stratum.Modules.Notification.Contracts.DTOs;

/// <summary>
/// Envoi des alertes de supervision par email (SUP03, F12 §5.3). Compose un message FRANÇAIS actionnable à
/// partir de l'alerte et l'ENFILE via le pipeline du module Notification (<see cref="IJobQueue"/> →
/// <c>EmailSendJobHandler</c> → <c>IEmailTransport</c>) : la mise en file + le retry du job rendent l'envoi
/// SMTP non bloquant. Destinataires (F12 §5.3) : opérateur d'instance = TOUTES les alertes ; contact du
/// tenant = alertes CRITIQUES uniquement, si configuré (<c>ContactEmailAlerte</c>) ET activé
/// (<c>AlertTenantContact</c>). Ne porte AUCUN secret (le mot de passe SMTP vit dans le transport, Host).
/// <para>Ne lève JAMAIS (fire-and-log) hors annulation : une notification ne casse pas l'évaluation des
/// règles (SUP03 §4). L'anti-spam est porté par le moteur (notification aux seules transitions).</para>
/// </summary>
internal sealed partial class AlertEmailNotifier : IAlertNotifier, IAlertDigestSender
{
    private const string LanguageCode = "fr";
    private const string RaisedTemplateCode = "supervision.alert.raised";
    private const string ResolvedTemplateCode = "supervision.alert.resolved";
    private const string DigestTemplateCode = "supervision.alert.digest";

    private readonly IJobQueue _jobQueue;
    private readonly ITenantSettingsQueries _tenantSettings;
    private readonly IAlertQueries _alertQueries;
    private readonly SupervisionNotificationOptions _options;
    private readonly ILogger<AlertEmailNotifier> _logger;

    public AlertEmailNotifier(
        IJobQueue jobQueue,
        ITenantSettingsQueries tenantSettings,
        IAlertQueries alertQueries,
        IOptions<SupervisionNotificationOptions> options,
        ILogger<AlertEmailNotifier> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _jobQueue = jobQueue;
        _tenantSettings = tenantSettings;
        _alertQueries = alertQueries;
        _options = options.Value;
        _logger = logger;
    }

    public async Task NotifyRaisedAsync(Alert alert, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alert);

        try
        {
            var companyId = await _tenantSettings.GetCurrentCompanyId(cancellationToken).ConfigureAwait(false);
            var subject = string.Format(
                CultureInfo.InvariantCulture,
                "[Liakont] Alerte {0} — tenant {1}",
                SeverityWord(alert.Severity),
                alert.TenantId);
            var body = BuildRaisedBody(alert);

            // Opérateur d'instance : TOUTES les alertes (F12 §5.3).
            if (!string.IsNullOrWhiteSpace(_options.OperatorEmail))
            {
                await EnqueueEmailAsync(_options.OperatorEmail, subject, body, RaisedTemplateCode, companyId, alert.RuleKey, cancellationToken).ConfigureAwait(false);
            }

            // Contact du tenant : alertes CRITIQUES uniquement, si configuré ET activé (F12 §5.3).
            if (alert.Severity == AlertSeverity.Critical && companyId is Guid tenantCompanyId)
            {
                var contact = await ResolveTenantContactAsync(tenantCompanyId, cancellationToken).ConfigureAwait(false);
                if (contact is not null)
                {
                    await EnqueueEmailAsync(contact, subject, body, RaisedTemplateCode, companyId, alert.RuleKey, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fire-and-log : un échec de notification ne casse jamais l'évaluation des règles (SUP03 §4).
            LogNotifyFailed(_logger, alert.RuleKey, alert.TenantId, ex);
        }
    }

    public async Task NotifyResolvedAsync(Alert alert, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alert);

        // Email de résolution OPTIONNEL (SUP03 §3) — à l'opérateur uniquement.
        if (!_options.SendResolutionEmails || string.IsNullOrWhiteSpace(_options.OperatorEmail))
        {
            return;
        }

        try
        {
            var companyId = await _tenantSettings.GetCurrentCompanyId(cancellationToken).ConfigureAwait(false);
            var subject = string.Format(
                CultureInfo.InvariantCulture,
                "[Liakont] Alerte résolue — tenant {0}",
                alert.TenantId);
            var body = BuildResolvedBody(alert);
            await EnqueueEmailAsync(_options.OperatorEmail, subject, body, ResolvedTemplateCode, companyId, alert.RuleKey, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogNotifyFailed(_logger, alert.RuleKey, alert.TenantId, ex);
        }
    }

    public async Task SendActiveAlertsDigestAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        // Digest OPTIONNEL (SUP03 §3) — à l'opérateur, seulement s'il y a au moins une alerte active.
        if (!_options.DailyDigestEnabled || string.IsNullOrWhiteSpace(_options.OperatorEmail))
        {
            return;
        }

        try
        {
            var active = await _alertQueries.ListActiveAsync(cancellationToken).ConfigureAwait(false);
            if (active.Count == 0)
            {
                return;
            }

            var companyId = await _tenantSettings.GetCurrentCompanyId(cancellationToken).ConfigureAwait(false);
            var subject = string.Format(
                CultureInfo.InvariantCulture,
                "[Liakont] Récapitulatif des alertes actives — tenant {0} ({1})",
                tenantId,
                active.Count);
            var body = BuildDigestBody(tenantId, active);
            await EnqueueEmailAsync(_options.OperatorEmail, subject, body, DigestTemplateCode, companyId, "digest", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogDigestFailed(_logger, tenantId, ex);
        }
    }

    private static string BuildRaisedBody(Alert alert)
    {
        var format = "Une alerte de supervision a été déclenchée sur votre instance Liakont.\n\n"
            + "Tenant : {0}\nGravité : {1}\nRègle : {2}\nDéclenchée le : {3}\n\n"
            + "{4}\n\n"
            + "Consultez le dashboard de supervision pour le détail et l'acquittement.";

        return string.Format(
            CultureInfo.InvariantCulture,
            format,
            alert.TenantId,
            SeverityLabel(alert.Severity),
            alert.RuleKey,
            FormatUtc(alert.TriggeredUtc),
            alert.Detail ?? "(aucun détail fourni)");
    }

    private static string BuildResolvedBody(Alert alert)
    {
        var format = "Une alerte de supervision s'est résolue (la condition a disparu).\n\n"
            + "Tenant : {0}\nGravité : {1}\nRègle : {2}\nDéclenchée le : {3}\nRésolue le : {4}\n\n"
            + "Aucune action n'est requise. Ce message vous est envoyé car les notifications de résolution sont activées.";

        return string.Format(
            CultureInfo.InvariantCulture,
            format,
            alert.TenantId,
            SeverityLabel(alert.Severity),
            alert.RuleKey,
            FormatUtc(alert.TriggeredUtc),
            FormatUtc(alert.ResolvedUtc ?? alert.TriggeredUtc));
    }

    private static string BuildDigestBody(string tenantId, IReadOnlyList<AlertDto> active)
    {
        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture, $"Récapitulatif quotidien des alertes de supervision ACTIVES.\n\n");
        builder.Append(CultureInfo.InvariantCulture, $"Tenant : {tenantId}\n");
        builder.Append(CultureInfo.InvariantCulture, $"Nombre d'alertes actives : {active.Count}\n\n");

        foreach (var alert in active)
        {
            builder.Append(CultureInfo.InvariantCulture, $"- {SeverityLabelFromName(alert.Severity)} | {alert.RuleKey} | déclenchée le {FormatUtc(alert.TriggeredUtc)}\n");
            if (!string.IsNullOrWhiteSpace(alert.Detail))
            {
                builder.Append(CultureInfo.InvariantCulture, $"  {alert.Detail}\n");
            }
        }

        builder.Append("\nConsultez le dashboard de supervision pour le détail et l'acquittement.");
        return builder.ToString();
    }

    private static string SeverityWord(AlertSeverity severity) =>
        severity == AlertSeverity.Critical ? "critique" : "avertissement";

    private static string SeverityLabel(AlertSeverity severity) =>
        severity == AlertSeverity.Critical ? "🔴 Critique" : "🟠 Avertissement";

    private static string SeverityLabelFromName(string severityName) =>
        string.Equals(severityName, nameof(AlertSeverity.Critical), StringComparison.OrdinalIgnoreCase)
            ? "🔴 Critique"
            : "🟠 Avertissement";

    private static string FormatUtc(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    [LoggerMessage(Level = LogLevel.Information, Message = "Alerte mise en file pour notification : {Recipient} (règle {RuleKey}).")]
    private static partial void LogEnqueued(ILogger logger, string recipient, string ruleKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Échec de notification d'alerte (règle {RuleKey}, tenant {TenantId}) — l'évaluation continue.")]
    private static partial void LogNotifyFailed(ILogger logger, string ruleKey, string tenantId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Échec d'envoi du digest de supervision (tenant {TenantId}).")]
    private static partial void LogDigestFailed(ILogger logger, string tenantId, Exception exception);

    private async Task EnqueueEmailAsync(
        string recipient,
        string subject,
        string body,
        string templateCode,
        Guid? companyId,
        string ruleKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(recipient))
        {
            return;
        }

        var payload = new EmailSendJobPayload
        {
            RecipientEmail = recipient,
            Subject = subject,
            Body = body,
            TemplateCode = templateCode,
            LanguageCode = LanguageCode,
            CompanyId = companyId,
        };

        await _jobQueue.EnqueueAsync(payload, companyId: companyId, ct: cancellationToken).ConfigureAwait(false);
        LogEnqueued(_logger, recipient, ruleKey);
    }

    private async Task<string?> ResolveTenantContactAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var thresholds = await _tenantSettings.GetAlertThresholds(companyId, cancellationToken).ConfigureAwait(false);
        if (thresholds is null || !thresholds.AlertTenantContact)
        {
            return null;
        }

        var profile = await _tenantSettings.GetTenantProfile(companyId, cancellationToken).ConfigureAwait(false);
        var email = profile?.ContactEmailAlerte;
        return string.IsNullOrWhiteSpace(email) ? null : email;
    }
}
