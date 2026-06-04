namespace Liakont.Modules.Archive.Tests.Unit;

using System;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Archive.Application;
using Liakont.Modules.Archive.Contracts;
using Liakont.Modules.Archive.Domain;
using Liakont.Modules.Archive.Tests.Unit.Doubles;
using Xunit;

public sealed class ArchiveServiceTests
{
    private const string PackageDir = "2026/05/F-2026-001/";

    private readonly InMemoryArchiveStore _store = new();
    private readonly FakeArchiveEntryStore _entryStore = new();

    private ArchiveService CreateService(string? tenant = ArchiveTestData.Tenant) =>
        new(_store, _entryStore, new StubTenantContext(tenant));

    [Fact]
    public async Task ArchiveIssuedDocument_WritesPackageAndSealsChainedEntry()
    {
        ArchiveService service = CreateService();

        ArchivePackageResult result = await service.ArchiveIssuedDocumentAsync(ArchiveTestData.PackageRequest());

        result.PackageHash.Should().MatchRegex("^[0-9a-f]{64}$");
        result.ChainHash.Should().Be(HashChain.Next(null, result.PackageHash));
        result.PackagePath.Should().Be(PackageDir + "manifest.json");

        // 5 fichiers de contenu (payload, réponse, html, facture-pa, bordereau) + manifest.
        _store.ObjectCount.Should().Be(6);
        _entryStore.Records.Should().ContainSingle();
    }

    [Fact]
    public async Task ArchiveIssuedDocument_UnresolvedTenant_Throws()
    {
        ArchiveService service = CreateService(tenant: null);
        Func<Task> act = () => service.ArchiveIssuedDocumentAsync(ArchiveTestData.PackageRequest());
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ArchiveIssuedDocument_AbsentPieceWithoutReason_Throws()
    {
        ArchiveService service = CreateService();
        ArchivePackageRequest request = ArchiveTestData.PackageRequest();
        ArchivePackageRequest invalid = new()
        {
            DocumentId = request.DocumentId,
            DocumentNumber = request.DocumentNumber,
            IssueDate = request.IssueDate,
            PayloadJson = request.PayloadJson,
            PaResponseJson = request.PaResponseJson,
            Readable = request.Readable,
            PaInvoice = null,
            PaInvoiceAbsenceReason = null,
            SourceDocument = request.SourceDocument,
        };

        Func<Task> act = () => service.ArchiveIssuedDocumentAsync(invalid);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task AddAddendum_ChainsOnTheDocumentPackage()
    {
        ArchiveService service = CreateService();
        ArchivePackageResult package = await service.ArchiveIssuedDocumentAsync(ArchiveTestData.PackageRequest());

        ArchivePackageResult addendum = await service.AddAddendumAsync(
            ArchiveTestData.AddendumRequest(package.DocumentId));

        addendum.ChainHash.Should().Be(HashChain.Next(package.ChainHash, addendum.PackageHash));
        addendum.PackagePath.Should().Be(PackageDir + "manifest-addendum-001.json");

        ArchiveIntegrityReport report = await service.VerifyTenantChainAsync();
        report.IsIntact.Should().BeTrue();
        report.EntryCount.Should().Be(2);
    }

    [Fact]
    public async Task VerifyTenantChain_IsIntact_ForHonestChain()
    {
        ArchiveService service = CreateService();
        await service.ArchiveIssuedDocumentAsync(ArchiveTestData.PackageRequest("F-2026-001"));
        await service.ArchiveIssuedDocumentAsync(ArchiveTestData.PackageRequest("F-2026-002"));

        ArchiveIntegrityReport report = await service.VerifyTenantChainAsync();

        report.IsIntact.Should().BeTrue();
        report.FirstBreakDetail.Should().BeNull();
        report.Entries.Should().OnlyContain(e => e.ContentValid && e.ChainValid);
    }

    [Fact]
    public async Task VerifyTenantChain_DetectsContentAlteration_AndCascades()
    {
        ArchiveService service = CreateService();
        await service.ArchiveIssuedDocumentAsync(ArchiveTestData.PackageRequest("F-2026-001"));
        await service.ArchiveIssuedDocumentAsync(ArchiveTestData.PackageRequest("F-2026-002"));

        // Altération directe d'une pièce du PREMIER paquet, en contournant le produit (attaquant backend).
        _store.Tamper(ArchiveTestData.Tenant, "2026/05/F-2026-001/payload.json", Encoding.UTF8.GetBytes("FAUX"));

        ArchiveIntegrityReport report = await service.VerifyTenantChainAsync();

        report.IsIntact.Should().BeFalse();
        report.Entries[0].ContentValid.Should().BeFalse();

        // La chaîne est rompue à partir de ce point : l'entrée suivante ne chaîne plus.
        report.Entries[1].ChainValid.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyTenantChain_DetectsAddendumAlteration()
    {
        ArchiveService service = CreateService();
        ArchivePackageResult package = await service.ArchiveIssuedDocumentAsync(ArchiveTestData.PackageRequest());
        await service.AddAddendumAsync(ArchiveTestData.AddendumRequest(package.DocumentId));

        _store.Tamper(ArchiveTestData.Tenant, PackageDir + "addendum-001-tax-report.xml", Encoding.UTF8.GetBytes("<faux/>"));

        ArchiveIntegrityReport report = await service.VerifyTenantChainAsync();

        report.IsIntact.Should().BeFalse();
        report.Entries[1].ContentValid.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyTenantChain_DetectsMissingPiece()
    {
        ArchiveService service = CreateService();
        await service.ArchiveIssuedDocumentAsync(ArchiveTestData.PackageRequest());

        _store.Remove(ArchiveTestData.Tenant, PackageDir + "payload.json");

        ArchiveIntegrityReport report = await service.VerifyTenantChainAsync();

        report.IsIntact.Should().BeFalse();
        report.Entries[0].ContentValid.Should().BeFalse();
    }
}
