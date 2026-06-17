namespace Liakont.Modules.Signature.Tests.Unit;

using FluentAssertions;
using Liakont.Modules.Archive.Contracts;
using Liakont.Modules.Signature.Application;
using Liakont.Modules.Signature.Contracts;
using Liakont.Modules.Signature.Domain.Entities;
using Liakont.Modules.Signature.Infrastructure.Drain;
using Liakont.Modules.Signature.Tests.Unit.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

/// <summary>
/// Drain WORM des webhooks de signature (ADR-0029 §5 ; INV-YOUSIGN-6) : une entrée liée à un document
/// télécharge la preuve via le fournisseur et la RAPATRIE en WORM via <c>Archive.Contracts</c> (jamais le
/// plug-in, jamais Archive.Domain) ; un événement orphelin est traité sans rapatriement ; un échec laisse
/// l'entrée re-tentable (jamais avalé).
/// </summary>
public sealed class SignatureWebhookDrainServiceTests
{
    private static readonly Guid Company = Guid.NewGuid();

    [Fact]
    public async Task Drains_event_downloads_proof_and_repatriates_to_worm()
    {
        var inbox = new FakeWebhookInbox(Item("evt-1", "sig-1"));
        var requests = new FakeRequestStore(Link("sig-1"));
        var archive = new FakeArchiveService();
        var service = BuildService(inbox, requests, archive, proofDownloadable: true);

        var processed = await service.DrainAsync();

        processed.Should().Be(1);
        archive.Addenda.Should().ContainSingle();
        archive.Addenda[0].DocumentNumber.Should().Be("FAC-2026-001");
        archive.Addenda[0].Kind.Should().Be("signature-yousign");
        inbox.Processed.Should().Contain(inbox.Items[0].Id);
    }

    [Fact]
    public async Task Orphan_event_without_request_link_is_marked_processed_without_worm()
    {
        var inbox = new FakeWebhookInbox(Item("evt-2", "unknown-ref"));
        var requests = new FakeRequestStore(link: null);
        var archive = new FakeArchiveService();
        var service = BuildService(inbox, requests, archive, proofDownloadable: true);

        var processed = await service.DrainAsync();

        processed.Should().Be(1);
        archive.Addenda.Should().BeEmpty("un événement orphelin n'a rien à rapatrier");
        inbox.Processed.Should().Contain(inbox.Items[0].Id);
    }

    [Fact]
    public async Task Proof_unavailable_leaves_entry_for_retry()
    {
        var inbox = new FakeWebhookInbox(Item("evt-3", "sig-3"));
        var requests = new FakeRequestStore(Link("sig-3"));
        var archive = new FakeArchiveService();
        var service = BuildService(inbox, requests, archive, proofDownloadable: false);

        var processed = await service.DrainAsync();

        processed.Should().Be(0, "la preuve indisponible n'est pas traitée");
        archive.Addenda.Should().BeEmpty();
        inbox.Processed.Should().BeEmpty();
        inbox.Failed.Should().Contain(inbox.Items[0].Id);
    }

    private static SignatureWebhookDrainService BuildService(
        FakeWebhookInbox inbox, FakeRequestStore requests, FakeArchiveService archive, bool proofDownloadable)
    {
        var capabilities = YousignLikeCapabilities(proofDownloadable);
        var registry = new FakeProviderRegistry(new FakeSignatureProvider(capabilities));
        var accounts = new FakeAccountStore(new SignatureProviderAccount("Yousign", Company.ToString(), "Sandbox"));

        return new SignatureWebhookDrainService(
            inbox, requests, accounts, registry, archive, NullLogger<SignatureWebhookDrainService>.Instance);
    }

    private static SignatureProviderCapabilities YousignLikeCapabilities(bool proofDownloadable) => new()
    {
        ProviderName = "Yousign",
        Mode = SignatureMode.Remote,
        CompletionTransport = CompletionTransport.Webhook,
        SupportedLevels = SignatureLevel.SES,
        SupportsProofDownload = proofDownloadable,
    };

    private static SignatureWebhookInboxItem Item(string eventId, string reference) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = Company,
        ProviderType = "Yousign",
        EventId = eventId,
        ProviderReference = reference,
        RawBody = [1, 2, 3],
        ReceivedAtUtc = DateTimeOffset.UtcNow,
    };

    private static SignatureRequestLink Link(string reference) => new()
    {
        CompanyId = Company,
        ProviderType = "Yousign",
        ProviderReference = reference,
        DocumentId = Guid.NewGuid(),
        DocumentNumber = "FAC-2026-001",
        IssueDate = new DateOnly(2026, 6, 16),
        Purpose = "MandateSignature",
        RequestedLevel = SignatureLevel.SES,
        CreatedAtUtc = DateTimeOffset.UtcNow,
    };
}
