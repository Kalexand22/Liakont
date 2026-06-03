namespace Stratum.Modules.Notification.Infrastructure.Handlers.Jobs;

using Microsoft.Extensions.Logging;
using Stratum.Modules.Job.Contracts;
using Stratum.Modules.Notification.Application;
using Stratum.Modules.Notification.Contracts;
using Stratum.Modules.Notification.Contracts.DTOs;

public sealed partial class DeliveryRetryJobHandler : IJobHandler<DeliveryRetryJobPayload>
{
    private readonly IEmailTransport _emailTransport;
    private readonly INotificationUnitOfWorkFactory _uowFactory;
    private readonly ILogger<DeliveryRetryJobHandler> _logger;

    public DeliveryRetryJobHandler(
        IEmailTransport emailTransport,
        INotificationUnitOfWorkFactory uowFactory,
        ILogger<DeliveryRetryJobHandler> logger)
    {
        _emailTransport = emailTransport;
        _uowFactory = uowFactory;
        _logger = logger;
    }

    public async Task HandleAsync(DeliveryRetryJobPayload payload, CancellationToken ct = default)
    {
        await using var uow = await _uowFactory.BeginAsync(ct);

        var record = await uow.GetDeliveryRecordByIdAsync(payload.DeliveryRecordId, ct);
        if (record is null)
        {
            LogRecordNotFound(_logger, payload.DeliveryRecordId);
            return;
        }

        try
        {
            await _emailTransport.SendAsync(payload.RecipientEmail, payload.Subject, payload.Body, ct);

            record.ClearFailureForRetry();
            record.MarkDelivered();
            await uow.UpdateDeliveryRecordAsync(record, ct);
            await uow.CommitAsync(ct);

            LogRetrySuccess(_logger, payload.DeliveryRecordId, payload.RecipientEmail);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogRetryFailed(_logger, payload.DeliveryRecordId, payload.RecipientEmail, ex);

            try
            {
                record.MarkFailed();
                await uow.UpdateDeliveryRecordAsync(record, ct);
                await uow.CommitAsync(ct);
            }
            catch (Exception updateEx)
            {
                LogRecordUpdateFailed(_logger, payload.DeliveryRecordId, updateEx);
            }

            throw;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Delivery record {RecordId} not found for retry")]
    private static partial void LogRecordNotFound(ILogger logger, Guid recordId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Delivery retry success for record {RecordId} to {Recipient}")]
    private static partial void LogRetrySuccess(ILogger logger, Guid recordId, string recipient);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Delivery retry failed for record {RecordId} to {Recipient}")]
    private static partial void LogRetryFailed(ILogger logger, Guid recordId, string recipient, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to update delivery record {RecordId} after retry failure")]
    private static partial void LogRecordUpdateFailed(ILogger logger, Guid recordId, Exception exception);
}
