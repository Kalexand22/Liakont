namespace Liakont.Modules.Signature.Tests.Integration;

using FluentAssertions;
using Liakont.Modules.Signature.Domain.Entities;
using Liakont.Modules.Signature.Infrastructure.Persistence;
using Liakont.Modules.Signature.Tests.Integration.Fixtures;
using Xunit;

/// <summary>
/// Isolation tenant cross-BASE de l'inbox (CLAUDE.md n°9 ; INV-YOUSIGN-4) sur DEUX bases tenant réelles : un
/// webhook persisté dans la base d'un tenant n'est JAMAIS visible (drainable) dans la base de l'autre.
/// </summary>
[Collection("SignatureMultiTenantIntegration")]
public sealed class SignatureWebhookInboxMultiTenantIntegrationTests
{
    private readonly SignatureMultiTenantFixture _fixture;

    public SignatureWebhookInboxMultiTenantIntegrationTests(SignatureMultiTenantFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Webhook_persisted_in_tenant_a_is_not_drainable_in_tenant_b()
    {
        var inboxA = new PostgresSignatureWebhookInbox(_fixture.CreateConnectionFactory(SignatureMultiTenantFixture.TenantA));
        var inboxB = new PostgresSignatureWebhookInbox(_fixture.CreateConnectionFactory(SignatureMultiTenantFixture.TenantB));
        var company = Guid.NewGuid();

        var item = new SignatureWebhookInboxItem
        {
            Id = Guid.NewGuid(),
            CompanyId = company,
            ProviderType = "Yousign",
            EventId = "evt-cross",
            ProviderReference = "sig-cross",
            RawBody = [1, 2, 3],
            ReceivedAtUtc = DateTimeOffset.UtcNow,
        };

        (await inboxA.EnqueueAsync(item)).Should().BeTrue();

        var pendingA = await inboxA.DrainPendingAsync(100);
        var pendingB = await inboxB.DrainPendingAsync(100);

        pendingA.Should().ContainSingle(i => i.Id == item.Id, "l'événement est dans la base du tenant A");
        pendingB.Should().NotContain(i => i.Id == item.Id, "il n'est jamais visible dans la base du tenant B");
    }
}
