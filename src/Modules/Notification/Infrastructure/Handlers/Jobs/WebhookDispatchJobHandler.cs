namespace Stratum.Modules.Notification.Infrastructure.Handlers.Jobs;

using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Events;
using Stratum.Modules.Job.Contracts;
using Stratum.Modules.Notification.Application;
using Stratum.Modules.Notification.Contracts.DTOs;
using Stratum.Modules.Notification.Contracts.Events;
using Stratum.Modules.Notification.Domain.Services;

public sealed partial class WebhookDispatchJobHandler : IJobHandler<WebhookDispatchJobPayload>
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly INotificationUnitOfWorkFactory _uowFactory;
    private readonly ILogger<WebhookDispatchJobHandler> _logger;

    public WebhookDispatchJobHandler(
        IHttpClientFactory httpClientFactory,
        INotificationUnitOfWorkFactory uowFactory,
        ILogger<WebhookDispatchJobHandler> logger)
    {
        _httpClientFactory = httpClientFactory;
        _uowFactory = uowFactory;
        _logger = logger;
    }

    public async Task HandleAsync(WebhookDispatchJobPayload payload, CancellationToken ct = default)
    {
        // Fetch subscription at dispatch time to get current secret and URL
        await using var uow = await _uowFactory.BeginAsync(ct);
        var subscription = await uow.GetWebhookSubscriptionByIdAsync(payload.SubscriptionId, ct);
        if (subscription is null)
        {
            LogSubscriptionNotFound(_logger, payload.SubscriptionId);
            return;
        }

        if (!subscription.IsActive)
        {
            LogSubscriptionInactive(_logger, payload.SubscriptionId);
            return;
        }

        var targetUrl = subscription.TargetUrl;

        try
        {
            var signature = WebhookSignature.Compute(payload.PayloadJson, subscription.Secret);

            using var client = _httpClientFactory.CreateClient("WebhookDispatch");
            using var request = new HttpRequestMessage(HttpMethod.Post, targetUrl);
            request.Content = new StringContent(payload.PayloadJson, Encoding.UTF8, "application/json");
            request.Headers.Add("X-Stratum-Signature", signature);
            request.Headers.Add("X-Stratum-Event", payload.EventType);

            using var response = await client.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogWebhookFailed(_logger, targetUrl, payload.EventType, ex);

            await PublishFailedEventAsync(payload, targetUrl, ex.Message);

            throw;
        }

        // Publish success event outside the try/catch to avoid emitting
        // a false dispatch_failed if only event publishing fails
        try
        {
            await PublishDispatchedEventAsync(payload, targetUrl, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogDispatchedEventError(_logger, ex);
        }

        LogWebhookDispatched(_logger, targetUrl, payload.EventType);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Webhook dispatched to {TargetUrl} (event: {EventType})")]
    private static partial void LogWebhookDispatched(ILogger logger, string targetUrl, string eventType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Webhook dispatch failed to {TargetUrl} (event: {EventType})")]
    private static partial void LogWebhookFailed(ILogger logger, string targetUrl, string eventType, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to publish webhook.dispatch.failed event")]
    private static partial void LogFailedEventError(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to publish webhook.dispatched event")]
    private static partial void LogDispatchedEventError(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Webhook subscription {SubscriptionId} not found, skipping dispatch")]
    private static partial void LogSubscriptionNotFound(ILogger logger, Guid subscriptionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Webhook subscription {SubscriptionId} is inactive, skipping dispatch")]
    private static partial void LogSubscriptionInactive(ILogger logger, Guid subscriptionId);

    private async Task PublishDispatchedEventAsync(WebhookDispatchJobPayload payload, string actualTargetUrl, CancellationToken ct)
    {
        await using var uow = await _uowFactory.BeginAsync(ct);
        await uow.CommitWithEventAsync(
            new IntegrationEvent<WebhookDispatchedV1>
            {
                EventId = Guid.NewGuid(),
                EventType = "notification.webhook.dispatched",
                OccurredAt = DateTimeOffset.UtcNow,
                CorrelationId = Guid.NewGuid(),
                ModuleSource = "notification",
                Version = 1,
                Payload = new WebhookDispatchedV1
                {
                    SubscriptionId = payload.SubscriptionId,
                    EventType = payload.EventType,
                    TargetUrl = actualTargetUrl,
                    DispatchedAt = DateTimeOffset.UtcNow,
                },
            },
            ct);
    }

    private async Task PublishFailedEventAsync(WebhookDispatchJobPayload payload, string actualTargetUrl, string errorMessage)
    {
        try
        {
            // Use CancellationToken.None to ensure failure event is recorded
            // even if the original token is expired
            await using var uow = await _uowFactory.BeginAsync(CancellationToken.None);
            await uow.CommitWithEventAsync(
                new IntegrationEvent<WebhookDispatchFailedV1>
                {
                    EventId = Guid.NewGuid(),
                    EventType = "notification.webhook.dispatch_failed",
                    OccurredAt = DateTimeOffset.UtcNow,
                    CorrelationId = Guid.NewGuid(),
                    ModuleSource = "notification",
                    Version = 1,
                    Payload = new WebhookDispatchFailedV1
                    {
                        SubscriptionId = payload.SubscriptionId,
                        EventType = payload.EventType,
                        TargetUrl = actualTargetUrl,
                        ErrorMessage = errorMessage,
                        FailedAt = DateTimeOffset.UtcNow,
                    },
                },
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            LogFailedEventError(_logger, ex);
        }
    }
}
