namespace Liakont.Console.Api.Tests.Integration;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

/// <summary>
/// Tests d'intégration in-process de l'endpoint OPÉRATEUR d'export de réversibilité d'un client choisi
/// (OPS06a, <c>GET /api/v1/clients/{tenantId}/tenant-export</c>) : garde supervision (403 sans), client
/// inconnu (404), dossier complet streamé pour le tenant CIBLE, et — surtout — VÉRIABILITÉ HORS PLATEFORME :
/// l'archive réellement exportée est validée par l'outil autonome <c>tools/verifier-integrite-archive.ps1</c>
/// (intègre → exit 0 ; falsifiée → exit 1), ce qui prouve que l'outil reproduit fidèlement le chaînage de la
/// plateforme et détecte l'altération.
/// </summary>
[Collection(ConsoleApiCollectionFixture.Name)]
public sealed class ClientExportEndpointIntegrationTests
{
    private readonly ConsoleApiFactory _factory;

    public ClientExportEndpointIntegrationTests(ConsoleApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task OperatorExport_Without_Supervision_Returns_403()
    {
        // Le lecteur (liakont.read) n'est ni superviseur ni super-admin : l'export opérateur lui est interdit.
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantArchive, ConsoleApiFactory.ReaderUserId);

        var response = await client.GetAsync($"/api/v1/clients/{ConsoleApiFactory.TenantArchive}/tenant-export");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OperatorExport_Unknown_Tenant_Returns_404()
    {
        // Super-admin (opérateur d'instance) : la garde passe, mais le client cible n'existe pas au registre.
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantArchive, ConsoleApiFactory.SystemAdminUserId, roles: "SystemAdmin");

        var response = await client.GetAsync("/api/v1/clients/client-inexistant/tenant-export");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OperatorExport_Streams_Complete_Dossier_Of_The_Selected_Tenant()
    {
        await _factory.ArchiveSampleDocumentAsync(ConsoleApiFactory.TenantArchive, "OPS06A-001", new DateOnly(2026, 9, 4));
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantArchive, ConsoleApiFactory.SystemAdminUserId, roles: "SystemAdmin");

        var response = await client.GetAsync($"/api/v1/clients/{ConsoleApiFactory.TenantArchive}/tenant-export");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");
        var names = await ReadZipEntryNamesAsync(response);
        names.Should().Contain(n => n.StartsWith("archive/", StringComparison.Ordinal));
        names.Should().Contain("tracking/index.json");
        names.Should().Contain("parametrage/profil.json");
        names.Should().Contain("journal/audit.json");
        names.Should().Contain("notice-reversibilite.txt");
    }

    [Fact]
    public async Task Exported_Archive_Is_Verifiable_Offline_By_The_Standalone_Tool()
    {
        await _factory.ArchiveSampleDocumentAsync(ConsoleApiFactory.TenantArchive, "OPS06A-VERIFY", new DateOnly(2026, 9, 5));
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantArchive, ConsoleApiFactory.SystemAdminUserId, roles: "SystemAdmin");

        var response = await client.GetAsync($"/api/v1/clients/{ConsoleApiFactory.TenantArchive}/tenant-export");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        string exportDir = Path.Combine(Path.GetTempPath(), "liakont-ops06a-verify", Guid.NewGuid().ToString("N"));
        try
        {
            await ExtractZipAsync(response, exportDir);

            // L'outil autonome valide l'archive RÉELLEMENT exportée : sa concordance prouve qu'il reproduit
            // exactement le chaînage de la plateforme (aucune règle réinventée).
            (int healthyExit, _) = RunIntegrityTool(exportDir);
            healthyExit.Should().Be(0, "l'archive fraîchement exportée doit être intègre hors plateforme");

            // Falsification d'une pièce : l'outil DOIT la détecter (anti faux-vert de l'outil lui-même).
            string payload = Directory
                .EnumerateFiles(exportDir, "payload.json", SearchOption.AllDirectories)
                .First();
            File.WriteAllText(payload, "{\"tampered\":true}");

            (int tamperedExit, _) = RunIntegrityTool(exportDir);
            tamperedExit.Should().Be(1, "une pièce falsifiée doit rompre la chaîne et faire échouer l'outil");
        }
        finally
        {
            if (Directory.Exists(exportDir))
            {
                Directory.Delete(exportDir, recursive: true);
            }
        }
    }

    private static (int ExitCode, string Output) RunIntegrityTool(string exportDir)
    {
        string repoRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string tool = Path.Combine(repoRoot, "tools", "verifier-integrite-archive.ps1");
        File.Exists(tool).Should().BeTrue($"l'outil de vérification doit exister : {tool}");

        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{tool}\" -ExportPath \"{exportDir}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)!;
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }

    private static async Task ExtractZipAsync(System.Net.Http.HttpResponseMessage response, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        zip.ExtractToDirectory(targetDir);
    }

    private static async Task<List<string>> ReadZipEntryNamesAsync(System.Net.Http.HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        return zip.Entries.Select(e => e.FullName).ToList();
    }
}
