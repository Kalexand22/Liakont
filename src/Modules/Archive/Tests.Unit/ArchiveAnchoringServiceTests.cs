namespace Liakont.Modules.Archive.Tests.Unit;

using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Archive.Application;
using Liakont.Modules.Archive.Domain;
using Liakont.Modules.Archive.Infrastructure;
using Liakont.Modules.Archive.Tests.Unit.Doubles;
using Xunit;

/// <summary>Tests du service d'ancrage de la tête de chaîne (TRK06) : preuve archivée, idempotence, modes.</summary>
public sealed class ArchiveAnchoringServiceTests
{
    private readonly InMemoryArchiveStore _store = new();
    private readonly FakeArchiveEntryStore _entryStore = new();
    private readonly FakeArchiveAnchorStore _anchorStore = new();

    private ArchiveAnchoringService Create(ITimestampAnchor anchor) =>
        new(_entryStore, _anchorStore, _store, anchor, new StubTenantContext(ArchiveTestData.Tenant));

    private async Task SeedChainAsync()
    {
        var archiveService = new ArchiveService(_store, _entryStore, new StubTenantContext(ArchiveTestData.Tenant));
        await archiveService.ArchiveIssuedDocumentAsync(ArchiveTestData.PackageRequest());
    }

    [Fact]
    public async Task Anchor_Rfc3161_WritesProofAndIndexes()
    {
        using var tsa = new TestTimestampAuthority();
        await SeedChainAsync();
        ArchiveAnchoringService service = Create(new Rfc3161TimestampAnchor(FakeTsaClient.Backed(tsa)));

        AnchoringOutcome outcome = await service.AnchorChainHeadAsync();

        outcome.Status.Should().Be(AnchoringStatus.Anchored);
        outcome.Record.Should().NotBeNull();
        outcome.Record!.Method.Should().Be(TimestampAnchorMethod.Rfc3161);
        outcome.Record.ProofPath.Should().StartWith("_anchors/");
        _anchorStore.Records.Should().ContainSingle();
        (await _store.ExistsAsync(ArchiveTestData.Tenant, outcome.Record.ProofPath!)).Should().BeTrue();
    }

    [Fact]
    public async Task Anchor_IsIdempotent_ForSameHead()
    {
        using var tsa = new TestTimestampAuthority();
        await SeedChainAsync();
        var tsaClient = FakeTsaClient.Backed(tsa);
        ArchiveAnchoringService service = Create(new Rfc3161TimestampAnchor(tsaClient));

        await service.AnchorChainHeadAsync();
        AnchoringOutcome second = await service.AnchorChainHeadAsync();

        second.Status.Should().Be(AnchoringStatus.AlreadyAnchored);
        _anchorStore.Records.Should().ContainSingle();
        tsaClient.CallCount.Should().Be(1); // pas de second appel TSA pour une tête déjà ancrée
    }

    [Fact]
    public async Task Anchor_NoAnchor_IsNoOp()
    {
        await SeedChainAsync();
        ArchiveAnchoringService service = Create(new NoAnchorTimestampAnchor());

        AnchoringOutcome outcome = await service.AnchorChainHeadAsync();

        outcome.Status.Should().Be(AnchoringStatus.NotAnchoredByConfiguration);
        _anchorStore.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task Anchor_OpenTimestamps_NotOperational_DoesNotThrow()
    {
        await SeedChainAsync();
        ArchiveAnchoringService service = Create(new OpenTimestampsTimestampAnchor());

        AnchoringOutcome outcome = await service.AnchorChainHeadAsync();

        outcome.Status.Should().Be(AnchoringStatus.NotAnchoredByConfiguration);
        outcome.Detail.Should().Contain("ADR-0010");
        _anchorStore.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task Anchor_EmptyChain_NothingToAnchor()
    {
        using var tsa = new TestTimestampAuthority();
        ArchiveAnchoringService service = Create(new Rfc3161TimestampAnchor(FakeTsaClient.Backed(tsa)));

        AnchoringOutcome outcome = await service.AnchorChainHeadAsync();

        outcome.Status.Should().Be(AnchoringStatus.NothingToAnchor);
    }
}
