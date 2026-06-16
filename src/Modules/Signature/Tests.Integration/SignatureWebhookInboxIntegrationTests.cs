namespace Liakont.Modules.Signature.Tests.Integration;

using FluentAssertions;
using Liakont.Modules.Signature.Domain.Entities;
using Liakont.Modules.Signature.Infrastructure.Persistence;
using Liakont.Modules.Signature.Tests.Integration.Fixtures;
using Xunit;

/// <summary>
/// Inbox DURABLE des webhooks sur une vraie base PostgreSQL (ADR-0029 §4 ; INV-YOUSIGN-4/5) : durabilité
/// (un événement persisté survit jusqu'au drain — crash après 2xx ne le perd pas), idempotence
/// <c>(company_id, provider_type, event_id)</c>, et passage à l'état traité.
/// </summary>
[Collection("SignatureIntegration")]
public sealed class SignatureWebhookInboxIntegrationTests
{
    private readonly SignatureDatabaseFixture _fixture;

    public SignatureWebhookInboxIntegrationTests(SignatureDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Persisted_event_survives_until_drained_then_is_marked_processed()
    {
        var inbox = new PostgresSignatureWebhookInbox(_fixture.CreateConnectionFactory());
        var company = Guid.NewGuid();
        var item = Item(company, "evt-durable", "sig-d1");

        // Persistance AVANT 2xx.
        (await inbox.EnqueueAsync(item)).Should().BeTrue();

        // « Crash après 2xx » : on n'a pas drainé — l'événement est toujours là (durable).
        var pending = await inbox.DrainPendingAsync(100);
        pending.Should().ContainSingle(i => i.Id == item.Id && i.ProcessedAtUtc == null);

        // Après traitement, il n'est plus rendu par le drain.
        await inbox.MarkProcessedAsync(item.Id);
        var afterProcessing = await inbox.DrainPendingAsync(100);
        afterProcessing.Should().NotContain(i => i.Id == item.Id);
    }

    [Fact]
    public async Task Replay_of_same_event_is_idempotent()
    {
        var inbox = new PostgresSignatureWebhookInbox(_fixture.CreateConnectionFactory());
        var company = Guid.NewGuid();

        var first = await inbox.EnqueueAsync(Item(company, "evt-dup", "sig-x"));
        var replay = await inbox.EnqueueAsync(Item(company, "evt-dup", "sig-x"));

        first.Should().BeTrue("le premier événement est nouveau");
        replay.Should().BeFalse("un rejeu de la même clé (company, provider, event) est sans effet");

        var pending = await inbox.DrainPendingAsync(100);
        pending.Count(i => i.CompanyId == company && i.EventId == "evt-dup").Should().Be(1);
    }

    [Fact]
    public async Task Same_event_id_for_two_companies_is_not_a_duplicate()
    {
        var inbox = new PostgresSignatureWebhookInbox(_fixture.CreateConnectionFactory());
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        (await inbox.EnqueueAsync(Item(companyA, "shared-evt", "sig-a"))).Should().BeTrue();
        (await inbox.EnqueueAsync(Item(companyB, "shared-evt", "sig-b"))).Should().BeTrue(
            "l'idempotence est par (company_id, provider_type, event_id), jamais event_id seul");
    }

    private static SignatureWebhookInboxItem Item(Guid company, string eventId, string reference) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = company,
        ProviderType = "Yousign",
        EventId = eventId,
        ProviderReference = reference,
        RawBody = [10, 20, 30],
        ReceivedAtUtc = DateTimeOffset.UtcNow,
    };
}
