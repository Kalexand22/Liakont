namespace Stratum.Modules.Notification.Tests.Unit.Fakes;

using Stratum.Common.Abstractions.Events;
using Stratum.Modules.Notification.Application;
using Stratum.Modules.Notification.Domain.Entities;

internal sealed class FakeNotificationUnitOfWorkFactory : INotificationUnitOfWorkFactory
{
    private readonly EmailTemplate? _existingTemplate;
    private readonly WebhookSubscription? _existingWebhook;
    private readonly Dictionary<Guid, DeliverySla> _slaStore = new();

    public FakeNotificationUnitOfWorkFactory(
        EmailTemplate? existingTemplate = null,
        WebhookSubscription? existingWebhook = null,
        DeliverySla? existingSla = null)
    {
        _existingTemplate = existingTemplate;
        _existingWebhook = existingWebhook;
        if (existingSla is not null)
        {
            _slaStore[existingSla.Id] = existingSla;
        }
    }

    public EmailTemplate? LastInserted { get; private set; }

    public EmailTemplate? LastUpdated { get; private set; }

    public WebhookSubscription? LastInsertedWebhook { get; private set; }

    public WebhookSubscription? LastUpdatedWebhook { get; private set; }

    public Guid? LastDeletedWebhookId { get; private set; }

    public bool Committed { get; private set; }

    public Task<INotificationUnitOfWork> BeginAsync(CancellationToken ct = default)
        => Task.FromResult<INotificationUnitOfWork>(new FakeUnitOfWork(this));

    private sealed class FakeUnitOfWork : INotificationUnitOfWork
    {
        private readonly FakeNotificationUnitOfWorkFactory _parent;

        public FakeUnitOfWork(FakeNotificationUnitOfWorkFactory parent)
        {
            _parent = parent;
        }

        public Task InsertEmailTemplateAsync(EmailTemplate emailTemplate, CancellationToken ct = default)
        {
            _parent.LastInserted = emailTemplate;
            return Task.CompletedTask;
        }

        public Task UpdateEmailTemplateAsync(EmailTemplate emailTemplate, CancellationToken ct = default)
        {
            _parent.LastUpdated = emailTemplate;
            return Task.CompletedTask;
        }

        public Task<EmailTemplate?> GetEmailTemplateByIdAsync(Guid emailTemplateId, CancellationToken ct = default)
        {
            if (_parent._existingTemplate is not null && _parent._existingTemplate.Id == emailTemplateId)
            {
                return Task.FromResult<EmailTemplate?>(_parent._existingTemplate);
            }

            return Task.FromResult<EmailTemplate?>(null);
        }

        public Task InsertWebhookSubscriptionAsync(WebhookSubscription subscription, CancellationToken ct = default)
        {
            _parent.LastInsertedWebhook = subscription;
            return Task.CompletedTask;
        }

        public Task UpdateWebhookSubscriptionAsync(WebhookSubscription subscription, CancellationToken ct = default)
        {
            _parent.LastUpdatedWebhook = subscription;
            return Task.CompletedTask;
        }

        public Task DeleteWebhookSubscriptionAsync(Guid subscriptionId, CancellationToken ct = default)
        {
            _parent.LastDeletedWebhookId = subscriptionId;
            return Task.CompletedTask;
        }

        public Task<WebhookSubscription?> GetWebhookSubscriptionByIdAsync(Guid subscriptionId, CancellationToken ct = default)
        {
            if (_parent._existingWebhook is not null && _parent._existingWebhook.Id == subscriptionId)
            {
                return Task.FromResult<WebhookSubscription?>(_parent._existingWebhook);
            }

            return Task.FromResult<WebhookSubscription?>(null);
        }

        public Task InsertServiceDefinitionAsync(ServiceDefinition serviceDefinition, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task UpdateServiceDefinitionAsync(ServiceDefinition serviceDefinition, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task DeleteServiceDefinitionAsync(Guid serviceDefinitionId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<ServiceDefinition?> GetServiceDefinitionByIdAsync(Guid serviceDefinitionId, CancellationToken ct = default)
            => Task.FromResult<ServiceDefinition?>(null);

        public Task<bool> HasRoutingRulesForServiceCodeAsync(string serviceCode, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task InsertRoutingRuleAsync(RoutingRule routingRule, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task UpdateRoutingRuleAsync(RoutingRule routingRule, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task DeleteRoutingRuleAsync(Guid routingRuleId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<RoutingRule?> GetRoutingRuleByCodeAsync(string code, string entityType, CancellationToken ct = default)
            => Task.FromResult<RoutingRule?>(null);

        public Task<IReadOnlyList<RoutingRule>> GetActiveRoutingRulesAsync(string entityType, Guid? companyId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RoutingRule>>([]);

        public Task InsertDeliverySlaAsync(DeliverySla sla, CancellationToken ct = default)
        {
            _parent._slaStore[sla.Id] = sla;
            return Task.CompletedTask;
        }

        public Task UpdateDeliverySlaAsync(DeliverySla sla, CancellationToken ct = default)
        {
            _parent._slaStore[sla.Id] = sla;
            return Task.CompletedTask;
        }

        public Task DeleteDeliverySlaAsync(Guid id, CancellationToken ct = default)
        {
            _parent._slaStore.Remove(id);
            return Task.CompletedTask;
        }

        public Task<DeliverySla?> GetDeliverySlaByIdAsync(Guid id, CancellationToken ct = default)
        {
            _parent._slaStore.TryGetValue(id, out var sla);
            return Task.FromResult(sla);
        }

        public Task InsertDeliveryRecordAsync(DeliveryRecord record, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task UpdateDeliveryRecordAsync(DeliveryRecord record, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<DeliveryRecord?> GetDeliveryRecordByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult<DeliveryRecord?>(null);

        public Task CommitAsync(CancellationToken ct = default)
        {
            _parent.Committed = true;
            return Task.CompletedTask;
        }

        public Task CommitWithEventAsync<TPayload>(IntegrationEvent<TPayload> integrationEvent, CancellationToken ct = default)
        {
            _parent.Committed = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
