namespace Stratum.Modules.Notification.Infrastructure.Handlers.Jobs;

using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Events;
using Stratum.Modules.Job.Contracts;
using Stratum.Modules.Notification.Application;
using Stratum.Modules.Notification.Contracts;
using Stratum.Modules.Notification.Contracts.DTOs;
using Stratum.Modules.Notification.Contracts.Events;

public sealed partial class EmailSendJobHandler : IJobHandler<EmailSendJobPayload>
{
    private readonly IEmailTransport _emailTransport;
    private readonly INotificationUnitOfWorkFactory _uowFactory;
    private readonly ILogger<EmailSendJobHandler> _logger;

    public EmailSendJobHandler(
        IEmailTransport emailTransport,
        INotificationUnitOfWorkFactory uowFactory,
        ILogger<EmailSendJobHandler> logger)
    {
        _emailTransport = emailTransport;
        _uowFactory = uowFactory;
        _logger = logger;
    }

    public async Task HandleAsync(EmailSendJobPayload payload, CancellationToken ct = default)
    {
        try
        {
            await _emailTransport.SendAsync(payload.RecipientEmail, payload.Subject, payload.Body, ct);

            await PublishSentEventAsync(payload, ct);

            LogEmailSent(_logger, payload.RecipientEmail, payload.TemplateCode);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogEmailFailed(_logger, payload.RecipientEmail, payload.TemplateCode, ex);

            await PublishFailedEventAsync(payload, ex.Message, ct);

            throw;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Email sent to {Recipient} (template: {TemplateCode})")]
    private static partial void LogEmailSent(ILogger logger, string recipient, string templateCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Email send failed to {Recipient} (template: {TemplateCode})")]
    private static partial void LogEmailFailed(ILogger logger, string recipient, string templateCode, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to publish email.failed event")]
    private static partial void LogFailedEventError(ILogger logger, Exception exception);

    private async Task PublishSentEventAsync(EmailSendJobPayload payload, CancellationToken ct)
    {
        await using var uow = await _uowFactory.BeginAsync(ct);
        await uow.CommitWithEventAsync(
            new IntegrationEvent<EmailSentV1>
            {
                EventId = Guid.NewGuid(),
                EventType = "notification.email.sent",
                OccurredAt = DateTimeOffset.UtcNow,
                CorrelationId = Guid.NewGuid(),
                ModuleSource = "notification",
                Version = 1,
                Payload = new EmailSentV1
                {
                    RecipientEmail = payload.RecipientEmail,
                    TemplateCode = payload.TemplateCode,
                    LanguageCode = payload.LanguageCode,
                    CompanyId = payload.CompanyId,
                    SentAt = DateTimeOffset.UtcNow,
                },
            },
            ct);
    }

    private async Task PublishFailedEventAsync(EmailSendJobPayload payload, string errorMessage, CancellationToken ct)
    {
        try
        {
            await using var uow = await _uowFactory.BeginAsync(ct);
            await uow.CommitWithEventAsync(
                new IntegrationEvent<EmailFailedV1>
                {
                    EventId = Guid.NewGuid(),
                    EventType = "notification.email.failed",
                    OccurredAt = DateTimeOffset.UtcNow,
                    CorrelationId = Guid.NewGuid(),
                    ModuleSource = "notification",
                    Version = 1,
                    Payload = new EmailFailedV1
                    {
                        RecipientEmail = payload.RecipientEmail,
                        TemplateCode = payload.TemplateCode,
                        LanguageCode = payload.LanguageCode,
                        CompanyId = payload.CompanyId,
                        ErrorMessage = errorMessage,
                        FailedAt = DateTimeOffset.UtcNow,
                    },
                },
                ct);
        }
        catch (Exception ex)
        {
            LogFailedEventError(_logger, ex);
        }
    }
}
