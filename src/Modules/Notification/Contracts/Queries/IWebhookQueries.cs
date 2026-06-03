namespace Stratum.Modules.Notification.Contracts.Queries;

using Stratum.Modules.Notification.Contracts.DTOs;

public interface IWebhookQueries
{
    Task<IReadOnlyList<WebhookSubscriptionDto>> ListByEventType(string eventType, CancellationToken ct = default);

    Task<WebhookSubscriptionDto?> GetById(Guid subscriptionId, CancellationToken ct = default);

    Task<IReadOnlyList<WebhookSubscriptionDto>> ListByCompany(Guid companyId, CancellationToken ct = default);
}
