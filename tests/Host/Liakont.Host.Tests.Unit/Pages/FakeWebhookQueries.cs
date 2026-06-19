namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Stratum.Modules.Notification.Contracts.DTOs;
using Stratum.Modules.Notification.Contracts.Queries;

/// <summary>Double d'<see cref="IWebhookQueries"/> renvoyant des jeux fixés (tests bUnit Notification, RB6 P2).</summary>
internal sealed class FakeWebhookQueries : IWebhookQueries
{
    private readonly IReadOnlyList<WebhookSubscriptionDto> _subscriptions;

    public FakeWebhookQueries(IReadOnlyList<WebhookSubscriptionDto>? subscriptions = null) => _subscriptions = subscriptions ?? [];

    public Task<IReadOnlyList<WebhookSubscriptionDto>> ListByEventType(string eventType, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<WebhookSubscriptionDto>>(_subscriptions.Where(s => s.EventType == eventType).ToList());

    public Task<WebhookSubscriptionDto?> GetById(Guid subscriptionId, CancellationToken ct = default) =>
        Task.FromResult(_subscriptions.FirstOrDefault(s => s.Id == subscriptionId) ?? (_subscriptions.Count == 0 ? null : _subscriptions[0]));

    public Task<IReadOnlyList<WebhookSubscriptionDto>> ListByCompany(Guid companyId, CancellationToken ct = default) =>
        Task.FromResult(_subscriptions);
}
