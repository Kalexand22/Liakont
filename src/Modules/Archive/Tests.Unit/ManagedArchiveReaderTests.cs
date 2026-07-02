namespace Liakont.Modules.Archive.Tests.Unit;

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Archive.Application;
using Liakont.Modules.Archive.Contracts;
using Liakont.Modules.Archive.Domain;
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
    public async Task Verify_WhenPieceBytesTampered_ReportsAltered()
    {
        GedArchivePackageResult archived = await CreateWriter().ArchiveManagedDocumentAsync(Request());

        // Altération réelle du backend (contournement produit) : les octets de la pièce ne correspondent plus au
        // manifest scellé. On vérifie avec l'empreinte indexée CORRECTE (celle scellée à l'archivage) pour isoler
        // ce cas de la divergence d'index déjà couverte ci-dessus (INV-ARCH-GED-2).
        string piecePath = ArchivePackageLayout.Combine(
            GedArchivePackageLayout.PackageDirectory("bordereau", 2026, 5, "K-42"),
            "piece.pdf");
        _store.Tamper(Tenant, piecePath, Encoding.UTF8.GetBytes("%PDF-tampered"));

        GedArchiveIntegrityResult result = await CreateReader()
            .VerifyManagedPackageAsync(archived.ArchivePath, archived.ContentHash);

        result.Status.Should().Be(GedArchiveIntegrityStatus.Altered);
        result.Detail.Should().Contain("modifié");
    }

    [Fact]
    public async Task Verify_WhenContentPieceMissing_ReportsMissing()
    {
        GedArchivePackageResult archived = await CreateWriter().ArchiveManagedDocumentAsync(Request());

        string piecePath = ArchivePackageLayout.Combine(
            GedArchivePackageLayout.PackageDirectory("bordereau", 2026, 5, "K-42"),
            "piece.pdf");
        _store.Remove(Tenant, piecePath);

        GedArchiveIntegrityResult result = await CreateReader()
            .VerifyManagedPackageAsync(archived.ArchivePath, archived.ContentHash);

        result.Status.Should().Be(GedArchiveIntegrityStatus.Missing);
        result.Detail.Should().Contain("introuvable");
    }

    [Fact]
    public async Task Verify_WhenManifestCorrupt_ReportsAltered()
    {
        GedArchivePackageResult archived = await CreateWriter().ArchiveManagedDocumentAsync(Request());

        _store.Tamper(Tenant, archived.ArchivePath, Encoding.UTF8.GetBytes("{not valid json"));

        GedArchiveIntegrityResult result = await CreateReader()
            .VerifyManagedPackageAsync(archived.ArchivePath, archived.ContentHash);

        result.Status.Should().Be(GedArchiveIntegrityStatus.Altered);
        result.Detail.Should().Contain("illisible ou corrompu");
    }

    [Fact]
    public async Task Verify_WhenManifestArchiveKeyEmpty_ReportsAlteredWithoutThrowing()
    {
        // archiveKey="" est une chaîne JSON VALIDE, mais SanitizeSegment("") LÈVE ArgumentException dans
        // GedArchivePackageLayout.PackageDirectory : le reader doit la traduire en Altered, jamais la laisser
        // remonter jusqu'à la fiche (GDF08, INV-ARCH-GED-2 ; fail-closed n°3).
        await AssertTamperedManifestReportsAlteredWithoutThrowing(
            "{\"archiveKind\":\"bordereau\",\"archiveKey\":\"\",\"filedOn\":\"2026-05-12\",\"packageHash\":\"seal\",\"files\":[{\"name\":\"piece.pdf\"}]}");
    }

    [Fact]
    public async Task Verify_WhenManifestArchiveKindSanitizesEmpty_ReportsAlteredWithoutThrowing()
    {
        // archiveKind="/" s'assainit en segment VIDE (nom de base après le « / » = "") → ArgumentException.
        await AssertTamperedManifestReportsAlteredWithoutThrowing(
            "{\"archiveKind\":\"/\",\"archiveKey\":\"K-42\",\"filedOn\":\"2026-05-12\",\"packageHash\":\"seal\",\"files\":[{\"name\":\"piece.pdf\"}]}");
    }

    [Fact]
    public async Task Verify_WhenManifestPackageHashNotString_ReportsAlteredWithoutThrowing()
    {
        // packageHash numérique → GetString() lèverait InvalidOperationException (non rattrapée à l'origine).
        await AssertTamperedManifestReportsAlteredWithoutThrowing(
            "{\"archiveKind\":\"bordereau\",\"archiveKey\":\"K-42\",\"filedOn\":\"2026-05-12\",\"packageHash\":1234,\"files\":[{\"name\":\"piece.pdf\"}]}");
    }

    [Fact]
    public async Task Verify_WhenManifestFileNameEmpty_ReportsAlteredWithoutThrowing()
    {
        // files[].name="" passait le parse puis explosait HORS try dans ArchivePackageLayout.Combine.
        await AssertTamperedManifestReportsAlteredWithoutThrowing(
            "{\"archiveKind\":\"bordereau\",\"archiveKey\":\"K-42\",\"filedOn\":\"2026-05-12\",\"packageHash\":\"seal\",\"files\":[{\"name\":\"\"}]}");
    }

    [Fact]
    public async Task Verify_WhenManifestFileNameSanitizesEmpty_ReportsAlteredWithoutThrowing()
    {
        // Un nom non vide mais qui s'assainit en vide (« / ») exploserait AUSSI dans Combine (hors try) :
        // le pré-vol SanitizeSegment dans le parse le rattrape.
        await AssertTamperedManifestReportsAlteredWithoutThrowing(
            "{\"archiveKind\":\"bordereau\",\"archiveKey\":\"K-42\",\"filedOn\":\"2026-05-12\",\"packageHash\":\"seal\",\"files\":[{\"name\":\"/\"}]}");
    }

    // GDF08 : un manifest JSON VALIDE mais ALTÉRÉ (archiveKey/kind assainis en vide, packageHash non-chaîne, nom
    // de pièce vide/assaini en vide) doit rendre un verdict Altered — JAMAIS une exception qui atteindrait la
    // fiche /ged/document (le seul chemin rattrapé à l'origine était JsonException).
    private async Task AssertTamperedManifestReportsAlteredWithoutThrowing(string manifestJson)
    {
        GedArchivePackageResult archived = await CreateWriter().ArchiveManagedDocumentAsync(Request());
        _store.Tamper(Tenant, archived.ArchivePath, Encoding.UTF8.GetBytes(manifestJson));

        ManagedArchiveReader reader = CreateReader();
        GedArchiveIntegrityResult result = default!;
        Func<Task> act = async () => result = await reader.VerifyManagedPackageAsync(archived.ArchivePath, archived.ContentHash);

        await act.Should().NotThrowAsync("un manifest corrompu se traduit en verdict, jamais en exception (GDF08, INV-ARCH-GED-2)");
        result.Status.Should().Be(GedArchiveIntegrityStatus.Altered);
        result.Detail.Should().Contain("corrompu");
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
