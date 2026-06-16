namespace Liakont.Console.Api.Tests.Integration;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
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
            (int healthyExit, string healthyOut) = await RunIntegrityToolAsync(exportDir);
            healthyExit.Should().Be(0, "l'archive fraîchement exportée doit être intègre hors plateforme");
            healthyOut.Should().Contain("VERDICT=OK", "le marqueur machine confirme l'intégrité (pas un simple code 0)");

            // Falsification d'une pièce : l'outil DOIT la DÉTECTER (le marqueur VERDICT distingue la
            // détection d'un éventuel plantage du script — qui sortirait aussi en code 1).
            string payload = Directory
                .EnumerateFiles(exportDir, "payload.json", SearchOption.AllDirectories)
                .First();
            File.WriteAllText(payload, "{\"tampered\":true}");

            (int tamperedExit, string tamperedOut) = await RunIntegrityToolAsync(exportDir);
            tamperedExit.Should().Be(1, "une pièce falsifiée doit faire échouer l'outil");
            tamperedOut.Should().Contain("VERDICT=TAMPERED", "l'échec doit venir de la DÉTECTION d'altération, pas d'un plantage");
        }
        finally
        {
            if (Directory.Exists(exportDir))
            {
                Directory.Delete(exportDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Exported_Archive_With_Addendum_Is_Verifiable_Offline()
    {
        // Couvre la branche addendum de l'outil (manifest-addendum + empreinte = sha du fichier d'addendum).
        Guid docId = await _factory.ArchiveSampleDocumentAsync(ConsoleApiFactory.TenantArchive, "OPS06A-ADD", new DateOnly(2026, 9, 6));
        await _factory.AddArchiveAddendumAsync(ConsoleApiFactory.TenantArchive, docId, "OPS06A-ADD", new DateOnly(2026, 9, 6));
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantArchive, ConsoleApiFactory.SystemAdminUserId, roles: "SystemAdmin");

        var response = await client.GetAsync($"/api/v1/clients/{ConsoleApiFactory.TenantArchive}/tenant-export");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        string exportDir = Path.Combine(Path.GetTempPath(), "liakont-ops06a-add", Guid.NewGuid().ToString("N"));
        try
        {
            await ExtractZipAsync(response, exportDir);

            (int okExit, string okOut) = await RunIntegrityToolAsync(exportDir);
            okExit.Should().Be(0, "l'archive avec addendum doit être intègre hors plateforme");
            okOut.Should().Contain("VERDICT=OK");

            // Falsifie la pièce d'addendum : l'outil doit le détecter sur cette branche aussi.
            string addendum = Directory
                .EnumerateFiles(exportDir, "addendum-*", SearchOption.AllDirectories)
                .First();
            File.WriteAllText(addendum, "tampered-addendum");

            (int tamperedExit, string tamperedOut) = await RunIntegrityToolAsync(exportDir);
            tamperedExit.Should().Be(1);
            tamperedOut.Should().Contain("VERDICT=TAMPERED");
        }
        finally
        {
            if (Directory.Exists(exportDir))
            {
                Directory.Delete(exportDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Partial_Fiscal_Export_Is_Reported_Incomplete_Not_Tampered()
    {
        // Un export PARTIEL (un seul document, via l'export d'audit) ne contient pas la tête de chaîne du
        // coffre : l'outil doit le signaler (VERDICT=INCOMPLETE, code 0), JAMAIS conclure à une altération.
        await _factory.ArchiveSampleDocumentAsync(ConsoleApiFactory.TenantArchive, "OPS06A-PARENT", new DateOnly(2026, 9, 7));
        Guid laterDoc = await _factory.ArchiveSampleDocumentAsync(ConsoleApiFactory.TenantArchive, "OPS06A-LATER", new DateOnly(2026, 9, 8));
        using var reader = _factory.CreateClient(ConsoleApiFactory.TenantArchive, ConsoleApiFactory.ReaderUserId);

        var response = await reader.GetAsync($"/api/v1/documents/{laterDoc}/audit-export");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        string exportDir = Path.Combine(Path.GetTempPath(), "liakont-ops06a-partial", Guid.NewGuid().ToString("N"));
        try
        {
            await ExtractZipAsync(response, exportDir);

            (int exit, string output) = await RunIntegrityToolAsync(exportDir);
            exit.Should().Be(0, "un export partiel intègre n'est pas une altération");
            output.Should().Contain("VERDICT=INCOMPLETE", "l'absence de la tête de chaîne d'un export partiel est signalée, pas traitée comme une corruption");
        }
        finally
        {
            if (Directory.Exists(exportDir))
            {
                Directory.Delete(exportDir, recursive: true);
            }
        }
    }

    private static async Task<(int ExitCode, string Output)> RunIntegrityToolAsync(string exportDir)
    {
        string repoRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string tool = Path.Combine(repoRoot, "tools", "verifier-integrite-archive.ps1");
        File.Exists(tool).Should().BeTrue($"l'outil de vérification doit exister : {tool}");

        // Windows PowerShell 5.1 (cible historique de l'outillage Windows) ou PowerShell 7 (pwsh) ailleurs.
        string shell = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "powershell" : "pwsh";
        var psi = new ProcessStartInfo
        {
            FileName = shell,
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{tool}\" -ExportPath \"{exportDir}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)!;

        // Drainer les deux flux EN PARALLÈLE évite l'interblocage si l'un sature son pipe.
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(stdout, stderr);
        await process.WaitForExitAsync();
        return (process.ExitCode, stdout.Result + stderr.Result);
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
