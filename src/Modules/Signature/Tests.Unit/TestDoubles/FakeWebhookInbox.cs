namespace Liakont.Modules.Signature.Tests.Unit.TestDoubles;

using Liakont.Modules.Signature.Application;
using Liakont.Modules.Signature.Domain.Entities;

/// <summary>Double de test de <see cref="ISignatureWebhookInbox"/> : liste en mémoire + journal processed/failed.</summary>
internal sealed class FakeWebhookInbox : ISignatureWebhookInbox
{
    public FakeWebhookInbox(params SignatureWebhookInboxItem[] items)
    {
        Items = [.. items];
    }

    public List<SignatureWebhookInboxItem> Items { get; }

    public List<Guid> Processed { get; } = [];

    public List<Guid> Failed { get; } = [];

    public Task<bool> EnqueueAsync(SignatureWebhookInboxItem item, CancellationToken cancellationToken = default)
    {
        var duplicate = Items.Any(i =>
            i.CompanyId == item.CompanyId && i.ProviderType == item.ProviderType && i.EventId == item.EventId);
        if (duplicate)
        {
            return Task.FromResult(false);
        }

        Items.Add(item);
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<SignatureWebhookInboxItem>> DrainPendingAsync(
        int max, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SignatureWebhookInboxItem> pending = Items
            .Where(i => !Processed.Contains(i.Id))
            .Take(max)
            .ToList();
        return Task.FromResult(pending);
    }

    public Task MarkProcessedAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Processed.Add(id);
        return Task.CompletedTask;
    }

    public Task MarkFailedAsync(Guid id, string errorMessage, CancellationToken cancellationToken = default)
    {
        Failed.Add(id);
        return Task.CompletedTask;
    }
}
