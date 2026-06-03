namespace Stratum.Modules.Notification.Infrastructure;

using Microsoft.Extensions.Hosting;
using Stratum.Common.Infrastructure.Outbox;
using Stratum.Modules.Notification.Contracts.Events;

internal sealed class NotificationEventTypeRegistrar : IHostedService
{
    private readonly IEventTypeRegistry _registry;

    public NotificationEventTypeRegistrar(IEventTypeRegistry registry)
    {
        _registry = registry;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _registry
            .Register<EmailSentV1>("notification.email.sent")
            .Register<EmailFailedV1>("notification.email.failed")
            .Register<WebhookDispatchedV1>("notification.webhook.dispatched")
            .Register<WebhookDispatchFailedV1>("notification.webhook.dispatch_failed")
            .Register<DeliverySlaBreachedV1>("notification.delivery.sla_breached")
            .Register<RoutingRoutedV1>("notification.routing.routed");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
