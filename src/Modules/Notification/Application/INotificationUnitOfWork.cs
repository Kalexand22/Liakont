namespace Stratum.Modules.Notification.Application;

using Stratum.Common.Abstractions.Events;
using Stratum.Modules.Notification.Domain.Entities;

public interface INotificationUnitOfWork : IAsyncDisposable
{
    Task InsertEmailTemplateAsync(EmailTemplate emailTemplate, CancellationToken ct = default);

    Task UpdateEmailTemplateAsync(EmailTemplate emailTemplate, CancellationToken ct = default);

    Task<EmailTemplate?> GetEmailTemplateByIdAsync(Guid emailTemplateId, CancellationToken ct = default);

    Task InsertWebhookSubscriptionAsync(WebhookSubscription subscription, CancellationToken ct = default);

    Task UpdateWebhookSubscriptionAsync(WebhookSubscription subscription, CancellationToken ct = default);

    Task DeleteWebhookSubscriptionAsync(Guid subscriptionId, CancellationToken ct = default);

    Task<WebhookSubscription?> GetWebhookSubscriptionByIdAsync(Guid subscriptionId, CancellationToken ct = default);

    Task InsertServiceDefinitionAsync(ServiceDefinition serviceDefinition, CancellationToken ct = default);

    Task UpdateServiceDefinitionAsync(ServiceDefinition serviceDefinition, CancellationToken ct = default);

    Task DeleteServiceDefinitionAsync(Guid serviceDefinitionId, CancellationToken ct = default);

    Task<ServiceDefinition?> GetServiceDefinitionByIdAsync(Guid serviceDefinitionId, CancellationToken ct = default);

    Task<bool> HasRoutingRulesForServiceCodeAsync(string serviceCode, CancellationToken ct = default);

    Task InsertRoutingRuleAsync(RoutingRule routingRule, CancellationToken ct = default);

    Task UpdateRoutingRuleAsync(RoutingRule routingRule, CancellationToken ct = default);

    Task DeleteRoutingRuleAsync(Guid routingRuleId, CancellationToken ct = default);

    Task<RoutingRule?> GetRoutingRuleByCodeAsync(string code, string entityType, CancellationToken ct = default);

    Task<IReadOnlyList<RoutingRule>> GetActiveRoutingRulesAsync(string entityType, Guid? companyId, CancellationToken ct = default);

    Task InsertDeliverySlaAsync(DeliverySla sla, CancellationToken ct = default);

    Task UpdateDeliverySlaAsync(DeliverySla sla, CancellationToken ct = default);

    Task DeleteDeliverySlaAsync(Guid id, CancellationToken ct = default);

    Task<DeliverySla?> GetDeliverySlaByIdAsync(Guid id, CancellationToken ct = default);

    Task InsertDeliveryRecordAsync(DeliveryRecord record, CancellationToken ct = default);

    Task UpdateDeliveryRecordAsync(DeliveryRecord record, CancellationToken ct = default);

    Task<DeliveryRecord?> GetDeliveryRecordByIdAsync(Guid id, CancellationToken ct = default);

    Task CommitAsync(CancellationToken ct = default);

    Task CommitWithEventAsync<TPayload>(IntegrationEvent<TPayload> integrationEvent, CancellationToken ct = default);
}

public interface INotificationUnitOfWorkFactory
{
    Task<INotificationUnitOfWork> BeginAsync(CancellationToken ct = default);
}
