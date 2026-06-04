namespace Liakont.Modules.Archive.Tests.Unit;

using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Archive.Application;
using Liakont.Modules.Archive.Contracts;
using Liakont.Modules.Archive.Domain;
using Liakont.Modules.Archive.Infrastructure;
using Liakont.Modules.Archive.Tests.Unit.Doubles;
using Xunit;

/// <summary>Tests du vérifieur complet du coffre (TRK06) : chaîne + preuves d'ancrage.</summary>
public sealed class ArchiveVerifierTests
{
    private readonly InMemoryArchiveStore _store = new();
    private readonly FakeArchiveEntryStore _entryStore = new();
    private readonly FakeArchiveAnchorStore _anchorStore = new();
    private readonly ArchiveService _archiveService;

    public ArchiveVerifierTests()
    {
        _archiveService = new ArchiveService(_store, _entryStore, new StubTenantContext(ArchiveTestData.Tenant));
    }

    private ArchiveVerifier Create(ITimestampAnchor anchor) =>
        new(_archiveService, _entryStore, _anchorStore, _store, anchor, new StubTenantContext(ArchiveTestData.Tenant));

    private async Task<AnchoringOutcome> SeedAndAnchorAsync(TestTimestampAuthority tsa)
    {
        await _archiveService.ArchiveIssuedDocumentAsync(ArchiveTestData.PackageRequest());
        var anchoring = new ArchiveAnchoringService(
            _entryStore,
            _anchorStore,
            _store,
            new Rfc3161TimestampAnchor(FakeTsaClient.Backed(tsa)),
            new StubTenantContext(ArchiveTestData.Tenant));
        return await anchoring.AnchorChainHeadAsync();
    }

    [Fact]
    public async Task Verify_FullyVerified_WhenChainIntactAndAnchorValid()
    {
        using var tsa = new TestTimestampAuthority();
        await SeedAndAnchorAsync(tsa);
        ArchiveVerifier verifier = Create(new Rfc3161TimestampAnchor(FakeTsaClient.Backed(tsa)));

        ArchiveVerificationReport report = await verifier.VerifyTenantVaultAsync();

        report.Chain.IsIntact.Should().BeTrue();
        report.Anchors.Should().ContainSingle();
        report.Anchors[0].IsValid.Should().BeTrue();
        report.IsChainAnchored.Should().BeTrue();
        report.IsFullyVerified.Should().BeTrue();
    }

    [Fact]
    public async Task Verify_DetectsChainBreak()
    {
        using var tsa = new TestTimestampAuthority();
        await SeedAndAnchorAsync(tsa);
        _store.Tamper(ArchiveTestData.Tenant, "2026/05/F-2026-001/payload.json", Encoding.UTF8.GetBytes("FAUX"));
        ArchiveVerifier verifier = Create(new Rfc3161TimestampAnchor(FakeTsaClient.Backed(tsa)));

        ArchiveVerificationReport report = await verifier.VerifyTenantVaultAsync();

        report.Chain.IsIntact.Should().BeFalse();
        report.IsFullyVerified.Should().BeFalse();
    }

    [Fact]
    public async Task Verify_DetectsMissingAnchorProof()
    {
        using var tsa = new TestTimestampAuthority();
        AnchoringOutcome outcome = await SeedAndAnchorAsync(tsa);
        _store.Remove(ArchiveTestData.Tenant, outcome.Record!.ProofPath!);
        ArchiveVerifier verifier = Create(new Rfc3161TimestampAnchor(FakeTsaClient.Backed(tsa)));

        ArchiveVerificationReport report = await verifier.VerifyTenantVaultAsync();

        report.Chain.IsIntact.Should().BeTrue(); // la chaîne des paquets n'inclut pas les preuves d'ancrage
        report.Anchors[0].IsValid.Should().BeFalse();
        report.Anchors[0].Detail.Should().Contain("manquante");
        report.IsFullyVerified.Should().BeFalse();
    }

    [Fact]
    public async Task Verify_NoAnchors_FullyVerifiedWhenChainIntact()
    {
        await _archiveService.ArchiveIssuedDocumentAsync(ArchiveTestData.PackageRequest());
        ArchiveVerifier verifier = Create(new NoAnchorTimestampAnchor());

        ArchiveVerificationReport report = await verifier.VerifyTenantVaultAsync();

        report.Anchors.Should().BeEmpty();
        report.IsChainAnchored.Should().BeFalse();
        report.IsFullyVerified.Should().BeTrue();
    }
}
