namespace Liakont.Modules.Archive.Tests.Unit;

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Archive.Application;
using Liakont.Modules.Archive.Contracts;
using Liakont.Modules.Archive.Tests.Unit.Doubles;
using Xunit;

/// <summary>
/// Lecture d'un paquet GED du coffre (GED09b, F19 §6.7) : la vérification d'intégrité RE-LIT les octets réels
/// (via le coffre) et recalcule l'empreinte avec les mêmes primitives que l'écriture — l'ancre est le contenu du
/// coffre, jamais une valeur en base (INV-ARCH-GED-2). On couvre Verified / Altered / Missing / NotArchived et
/// la lecture de l'aperçu (présent/absent). Le paquet est d'abord rangé par la surface d'écriture réelle
/// (<see cref="GenericArchiveService"/>) pour que la re-lecture porte sur des octets authentiques.
/// </summary>
public sealed class ManagedArchiveReaderTests
{
    private const string Tenant = "acme";

    private readonly InMemoryArchiveStore _store = new();

    private static GedArchivePackageRequest Request(string? readableHtml = "<p>aperçu</p>") => new(
        ArchiveKind: "bordereau",
        ArchiveKey: "K-42",
        FiledOn: new DateOnly(2026, 5, 12),
        Contents: [new ArchiveAttachment("piece.pdf", "application/pdf", Encoding.UTF8.GetBytes("%PDF-ged"))],
        ReadableHtml: readableHtml,
        IndexAxes: []);

    private GenericArchiveService CreateWriter(string? tenant = Tenant) => new(_store, new StubTenantContext(tenant));

    private ManagedArchiveReader CreateReader(string? tenant = Tenant) => new(_store, new StubTenantContext(tenant));

    [Fact]
    public async Task Verify_RecomputesFromStoredBytes_AndConfirmsIntegrity()
    {
        GedArchivePackageResult archived = await CreateWriter().ArchiveManagedDocumentAsync(Request());

        GedArchiveIntegrityResult result = await CreateReader()
            .VerifyManagedPackageAsync(archived.ArchivePath, archived.ContentHash);

        result.Status.Should().Be(GedArchiveIntegrityStatus.Verified);
        result.RecomputedContentHash.Should().Be(archived.ContentHash);
        result.Detail.Should().BeNull();
    }

    [Fact]
    public async Task Verify_WhenIndexedHashDiffersFromRecomputed_ReportsAltered()
    {
        GedArchivePackageResult archived = await CreateWriter().ArchiveManagedDocumentAsync(Request());

        // Le coffre est intègre, mais l'empreinte INDEXÉE ne correspond pas : divergence détectée (index à
        // resynchroniser). On ne fait jamais confiance à la valeur en base seule.
        GedArchiveIntegrityResult result = await CreateReader()
            .VerifyManagedPackageAsync(archived.ArchivePath, "0000000000000000000000000000000000000000000000000000000000000000");

        result.Status.Should().Be(GedArchiveIntegrityStatus.Altered);
        result.RecomputedContentHash.Should().Be(archived.ContentHash);
        result.Detail.Should().Contain("index");
    }

    [Fact]
    public async Task Verify_WhenManifestMissing_ReportsMissing()
    {
        GedArchiveIntegrityResult result = await CreateReader()
            .VerifyManagedPackageAsync("_ged/bordereau/2026/05/absent/manifest.json", "somehash");

        result.Status.Should().Be(GedArchiveIntegrityStatus.Missing);
    }

    [Fact]
    public async Task Verify_WhenPathOrHashMissing_ReportsNotArchived()
    {
        ManagedArchiveReader reader = CreateReader();

        (await reader.VerifyManagedPackageAsync(null, null)).Status
            .Should().Be(GedArchiveIntegrityStatus.NotArchived);
        (await reader.VerifyManagedPackageAsync("_ged/bordereau/2026/05/K-42/manifest.json", null)).Status
            .Should().Be(GedArchiveIntegrityStatus.NotArchived);
    }

    [Fact]
    public async Task Verify_UnresolvedTenant_Throws()
    {
        Func<Task> act = () => CreateReader(tenant: null)
            .VerifyManagedPackageAsync("_ged/bordereau/2026/05/K-42/manifest.json", "hash");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ReadReadableHtml_ReturnsThePreview_WhenPresent()
    {
        GedArchivePackageResult archived = await CreateWriter().ArchiveManagedDocumentAsync(Request("<p>Bordereau lisible</p>"));

        string? html = await CreateReader().ReadManagedReadableHtmlAsync(archived.ArchivePath);

        html.Should().NotBeNull();
        html.Should().Contain("Bordereau lisible");
    }

    [Fact]
    public async Task ReadReadableHtml_ReturnsNull_WhenNoPreviewInPackage()
    {
        GedArchivePackageResult archived = await CreateWriter().ArchiveManagedDocumentAsync(Request(readableHtml: null));

        string? html = await CreateReader().ReadManagedReadableHtmlAsync(archived.ArchivePath);

        html.Should().BeNull();
    }

    [Fact]
    public async Task ReadReadableHtml_ReturnsNull_WhenManifestPathMissing()
    {
        (await CreateReader().ReadManagedReadableHtmlAsync(null)).Should().BeNull();
    }
}
