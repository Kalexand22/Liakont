namespace Stratum.Modules.Notification.Infrastructure.Services;

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Stratum.Modules.Job.Contracts;
using Stratum.Modules.Notification.Application;
using Stratum.Modules.Notification.Contracts;
using Stratum.Modules.Notification.Contracts.DTOs;
using Stratum.Modules.Notification.Contracts.Queries;
using Stratum.Modules.Notification.Domain.Entities;
using Stratum.Modules.Notification.Domain.Services;

internal sealed partial class NotificationSender : INotificationSender
{
    private readonly IEmailTemplateQueries _templateQueries;
    private readonly IJobQueue _jobQueue;
    private readonly IRoutingEngine _routingEngine;
    private readonly INotificationUnitOfWorkFactory _uowFactory;
    private readonly ILogger<NotificationSender> _logger;

    public NotificationSender(
        IEmailTemplateQueries templateQueries,
        IJobQueue jobQueue,
        IRoutingEngine routingEngine,
        INotificationUnitOfWorkFactory uowFactory,
        ILogger<NotificationSender> logger)
    {
        _templateQueries = templateQueries;
        _jobQueue = jobQueue;
        _routingEngine = routingEngine;
        _uowFactory = uowFactory;
        _logger = logger;
    }

    public async Task SendEmailAsync(
        string templateCode,
        string languageCode,
        string recipientEmail,
        IReadOnlyDictionary<string, string> placeholders,
        Guid? companyId = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            throw new ArgumentException("INV-NOTIF-006: Recipient email must not be empty.", nameof(recipientEmail));
        }

        var template = await _templateQueries.GetByCode(templateCode, languageCode, companyId, ct)
            ?? throw new InvalidOperationException(
                $"INV-NOTIF-007: Email template '{templateCode}' not found for language '{languageCode}'.");

        var subject = TemplateRenderer.Render(template.SubjectTemplate, placeholders);
        var body = TemplateRenderer.Render(template.BodyTemplate, placeholders);

        var payload = new EmailSendJobPayload
        {
            RecipientEmail = recipientEmail,
            Subject = subject,
            Body = body,
            TemplateCode = templateCode,
            LanguageCode = languageCode,
            CompanyId = companyId,
        };

        await _jobQueue.EnqueueAsync(payload, companyId: companyId, ct: ct);
    }

    public async Task SendRoutedNotificationsAsync(
        string entityType,
        string entityId,
        IReadOnlyDictionary<string, JsonElement> routingData,
        string templateCode,
        string languageCode,
        IReadOnlyDictionary<string, string> placeholders,
        Guid? companyId = null,
        CancellationToken ct = default)
    {
        var routingResults = await _routingEngine.EvaluateRoutingAsync(entityType, routingData, companyId, ct);

        if (routingResults.Count == 0)
        {
            LogNoRoutingMatches(_logger, entityType, entityId);
            return;
        }

        var template = await _templateQueries.GetByCode(templateCode, languageCode, companyId, ct)
            ?? throw new InvalidOperationException(
                $"INV-NOTIF-007: Email template '{templateCode}' not found for language '{languageCode}'.");

        await using var uow = await _uowFactory.BeginAsync(ct);
        var pendingPayloads = new List<(EmailSendJobPayload Payload, string ServiceCode)>();

        foreach (var match in routingResults)
        {
            var enrichedPlaceholders = new Dictionary<string, string>(placeholders)
            {
                ["SERVICE_NAME"] = match.RuleName,
                ["SERVICE_CODE"] = match.ServiceCode,
            };

            var subject = TemplateRenderer.Render(template.SubjectTemplate, enrichedPlaceholders);
            var body = TemplateRenderer.Render(template.BodyTemplate, enrichedPlaceholders);

            var record = DeliveryRecord.Create(
                templateCode,
                match.RecipientValue,
                entityType,
                entityId,
                companyId);

            await uow.InsertDeliveryRecordAsync(record, ct);

            pendingPayloads.Add((new EmailSendJobPayload
            {
                RecipientEmail = match.RecipientValue,
                Subject = subject,
                Body = body,
                TemplateCode = templateCode,
                LanguageCode = languageCode,
                CompanyId = companyId,
            }, match.ServiceCode));
        }

        await uow.CommitAsync(ct);

        foreach (var (payload, serviceCode) in pendingPayloads)
        {
            await _jobQueue.EnqueueAsync(payload, companyId: companyId, ct: ct);
            LogRoutedNotification(_logger, payload.RecipientEmail, serviceCode, entityType, entityId);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "No routing matches for entityType={EntityType} entityId={EntityId}")]
    private static partial void LogNoRoutingMatches(ILogger logger, string entityType, string entityId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Routed notification to {Recipient} (service={ServiceCode}) for entityType={EntityType} entityId={EntityId}")]
    private static partial void LogRoutedNotification(ILogger logger, string recipient, string serviceCode, string entityType, string entityId);
}
