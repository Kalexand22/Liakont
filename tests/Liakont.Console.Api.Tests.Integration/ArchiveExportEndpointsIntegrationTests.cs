namespace Liakont.Console.Api.Tests.Integration;

using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

/// <summary>
/// Tests d'intégration in-process des endpoints d'export d'audit / contrôle fiscal et de réversibilité de
/// la console (API03 ; module Archive, TRK05/06) : permissions (401/403, read vs settings), isolation
/// tenant, contenu des archives ZIP streamées (manifest, notice), export par document / par période,
/// export de réversibilité du tenant, et vérification d'intégrité à la demande (chaîne saine + altérée).
/// </summary>
[Collection(ConsoleApiCollectionFixture.Name)]
public sealed class ArchiveExportEndpointsIntegrationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ConsoleApiFactory _factory;

    public ArchiveExportEndpointsIntegrationTests(ConsoleApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AuditExport_Document_Without_Authentication_Returns_401()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantArchive);

        var response = await client.GetAsync($"/api/v1/documents/{Guid.NewGuid()}/audit-export");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AuditExport_Document_Without_Read_Permission_Returns_403()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantArchive, ConsoleApiFactory.NoPermissionUserId);

        var response = await client.GetAsync($"/api/v1/documents/{Guid.NewGuid()}/audit-export");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AuditExport_Document_Streams_Zip_With_Manifest_And_Notice()
    {
        Guid docId = await _factory.ArchiveSampleDocumentAsync(ConsoleApiFactory.TenantArchive, "EXP-DOC-001", new DateOnly(2026, 4, 10));
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantArchive, ConsoleApiFactory.ReaderUserId);

        var response = await client.GetAsync($"/api/v1/documents/{docId}/audit-export");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var names = await ReadZipEntryNamesAsync(response);
        names.Should().Contain("2026/04/EXP-DOC-001/manifest.json");
        names.Should().Contain("2026/04/EXP-DOC-001/payload.json");
        names.Should().Contain("notice-verification.txt");
        names.Should().Contain("rapport-integrite.json");
    }

    [Fact]
    public async Task AuditExport_Period_Selects_By_Range()
    {
        await _factory.ArchiveSampleDocumentAsync(ConsoleApiFactory.TenantArchive, "EXP-PER-001", new DateOnly(2026, 5, 15));
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantArchive, ConsoleApiFactory.ReaderUserId);

        var inRange = await ReadZipEntryNamesAsync(
            await client.GetAsync("/api/v1/audit-export?from=2026-05-01&to=2026-05-31"));
        var outOfRange = await ReadZipEntryNamesAsync(
            await client.GetAsync("/api/v1/audit-export?from=2030-01-01&to=2030-12-31"));

        inRange.Should().Contain("2026/05/EXP-PER-001/manifest.json");
        outOfRange.Should().NotContain("2026/05/EXP-PER-001/manifest.json");
    }

    [Fact]
    public async Task AuditExport_Period_Without_Any_Bound_Returns_400()
    {
        // Sans borne, l'export du coffre entier n'est PAS une lecture : il relève de /tenant-export (settings).
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantArchive, ConsoleApiFactory.ReaderUserId);

        var response = await client.GetAsync("/api/v1/audit-export");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AuditExport_Larger_Volume_Streams_All_Packages()
    {
        // Plusieurs paquets, exportés par une période dédiée (année 2027) : le ZIP est streamé (jamais
        // bufferisé en entier) et contient bien tous les paquets de la période.
        await _factory.ArchiveSampleDocumentAsync(ConsoleApiFactory.TenantArchive, "VOL-001", new DateOnly(2027, 1, 5));
        await _factory.ArchiveSampleDocumentAsync(ConsoleApiFactory.TenantArchive, "VOL-002", new DateOnly(2027, 1, 6));
        await _factory.ArchiveSampleDocumentAsync(ConsoleApiFactory.TenantArchive, "VOL-003", new DateOnly(2027, 2, 7));
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantArchive, ConsoleApiFactory.ReaderUserId);

        var names = await ReadZipEntryNamesAsync(
            await client.GetAsync("/api/v1/audit-export?from=2027-01-01&to=2027-12-31"));

        names.Should().Contain("2027/01/VOL-001/manifest.json");
        names.Should().Contain("2027/01/VOL-002/manifest.json");
        names.Should().Contain("2027/02/VOL-003/manifest.json");
    }

    [Fact]
    public async Task TenantExport_Without_Settings_Permission_Returns_403()
    {
        // Le lecteur a liakont.read mais PAS liakont.settings : la réversibilité lui est interdite.
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantArchive, ConsoleApiFactory.ReaderUserId);

        var response = await client.GetAsync("/api/v1/tenant-export");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task TenantExport_As_Settings_User_Streams_Complete_Dossier()
    {
        await _factory.ArchiveSampleDocumentAsync(ConsoleApiFactory.TenantArchive, "REV-001", new DateOnly(2026, 6, 9));
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantArchive, ConsoleApiFactory.SettingsUserId);

        var response = await client.GetAsync("/api/v1/tenant-export");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var names = await ReadZipEntryNamesAsync(response);
        names.Should().Contain("tracking/index.json");
        names.Should().Contain(n => n.StartsWith("tracking/documents-", StringComparison.Ordinal));

        // Le profil du tenant-arch n'est pas paramétré (CFG02 non joué pour ce tenant) : le volet
        // paramétrage est toujours présent (profil.json), le contenu masqué des comptes PA étant couvert
        // par le test unitaire avec société résolue (Build_MasksPaSecrets).
        names.Should().Contain("parametrage/profil.json");
        names.Should().Contain("journal/audit.json");
        names.Should().Contain("notice-reversibilite.txt");
        names.Should().Contain(n => n.StartsWith("archive/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ArchiveVerify_Without_Authentication_Returns_401()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantArchive);

        var response = await client.PostAsync("/api/v1/archive/verify", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ArchiveVerify_Without_Read_Permission_Returns_403()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantArchive, ConsoleApiFactory.NoPermissionUserId);

        var response = await client.PostAsync("/api/v1/archive/verify", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ArchiveVerify_Healthy_Vault_Reports_FullyVerified()
    {
        await _factory.ArchiveSampleDocumentAsync(ConsoleApiFactory.TenantArchive, "VER-OK-001", new DateOnly(2026, 3, 3));
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantArchive, ConsoleApiFactory.ReaderUserId);

        var report = await PostVerifyAsync(client);

        report.IsFullyVerified.Should().BeTrue();
    }

    [Fact]
    public async Task ArchiveVerify_Detects_Tampered_Chain()
    {
        // Tenant dédié : on archive, on vérifie SAIN, on falsifie un paquet, on revérifie → NON intègre.
        await _factory.ArchiveSampleDocumentAsync(ConsoleApiFactory.TenantArchiveTampered, "BAD-001", new DateOnly(2026, 7, 1));
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantArchiveTampered, ConsoleApiFactory.ReaderUserId);

        var before = await PostVerifyAsync(client);
        before.IsFullyVerified.Should().BeTrue("le coffre vient d'être scellé, il est intègre");

        _factory.TamperArchivedPayload(ConsoleApiFactory.TenantArchiveTampered).Should().BeTrue("un paquet doit exister à falsifier");

        var after = await PostVerifyAsync(client);
        after.IsFullyVerified.Should().BeFalse("un paquet a été altéré : la chaîne est rompue");
    }

    [Fact]
    public async Task AuditExport_Document_Is_Tenant_Isolated()
    {
        // Document archivé dans le tenant d'archive ; exporté depuis un AUTRE tenant, son paquet n'apparaît pas.
        Guid docId = await _factory.ArchiveSampleDocumentAsync(ConsoleApiFactory.TenantArchive, "ISO-001", new DateOnly(2026, 8, 8));
        using var clientB = _factory.CreateClient(ConsoleApiFactory.TenantB, ConsoleApiFactory.ReaderUserId);

        var response = await clientB.GetAsync($"/api/v1/documents/{docId}/audit-export");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var names = await ReadZipEntryNamesAsync(response);
        names.Should().NotContain("2026/08/ISO-001/manifest.json", "le paquet appartient à un autre tenant");
    }

    private static async Task<VerifyReport> PostVerifyAsync(HttpClient client)
    {
        var response = await client.PostAsync("/api/v1/archive/verify", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var report = await response.Content.ReadFromJsonAsync<VerifyReport>(JsonOptions);
        report.Should().NotBeNull();
        return report!;
    }

    private static async Task<List<string>> ReadZipEntryNamesAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        return zip.Entries.Select(e => e.FullName).ToList();
    }

    private sealed record VerifyReport(bool IsFullyVerified, bool IsChainAnchored, string Summary);
}
