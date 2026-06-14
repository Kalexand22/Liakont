namespace Liakont.Host.Tests.Unit.MultiTenancy;

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

/// <summary>
/// Cohérence des TROIS sources du <c>company_id</c> du tenant <c>default</c> (ADR-0021 §2c / RLM02) :
/// le mapper du realm-export (jeton émis), <c>DevTenantSeed:CompanyId</c> (amorçage du registre) et le
/// backfill de la migration V017 (registre <c>outbox.tenants</c>) DOIVENT coïncider — sinon le résolveur
/// autoritaire <c>company_id(jeton) → tenant</c> ne résout pas le default, ou résout un mauvais tenant.
/// Test NOMMÉ exigé par l'acceptance RLM02.
/// </summary>
public sealed class DefaultCompanyIdCoherenceTests
{
    [Fact]
    public void Default_CompanyId_Coincides_Across_Config_Migration_And_Realms()
    {
        var fromConfig = ReadDevTenantSeedCompanyId();
        var fromMigration = ReadV017BackfillCompanyId();

        // (1) Config DevTenantSeed ↔ (2) backfill V017 : le registre amorcé/backfillé porte la valeur
        // que l'amorçage de dev configure.
        fromMigration.Should().Be(
            fromConfig,
            "le backfill V017 du tenant default doit valoir DevTenantSeed:CompanyId (sinon registre incohérent)");

        // (3) Realms (dev + E2E) : au moins un utilisateur de tenant porte EXACTEMENT cette valeur,
        // donc le jeton émis résout bien le tenant default via le registre.
        foreach (var realmPath in new[]
                 {
                     "deploy/docker/keycloak/realm-export.json",
                     "tests/Liakont.Tests.E2E/Fixtures/keycloak-e2e-realm.json",
                 })
        {
            var realmCompanyIds = ReadRealmUserCompanyIds(realmPath);
            realmCompanyIds.Should().Contain(
                fromConfig,
                $"le realm « {realmPath} » doit émettre le company_id du default ({fromConfig}) sur ses utilisateurs de tenant");
        }
    }

    private static string ReadDevTenantSeedCompanyId()
    {
        var path = RepoFile("src/Host/Liakont.Host/appsettings.Development.json");
        using var doc = JsonDocument.Parse(
            File.ReadAllText(path),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

        var value = doc.RootElement.GetProperty("DevTenantSeed").GetProperty("CompanyId").GetString();
        value.Should().NotBeNullOrWhiteSpace("DevTenantSeed:CompanyId doit être configuré");
        return Normalize(value!);
    }

    private static string ReadV017BackfillCompanyId()
    {
        var path = RepoFile("src/Common/Infrastructure/Migrations/V017__enforce_company_id_on_tenants.sql");
        var sql = File.ReadAllText(path);

        // V017 ne contient qu'un seul littéral GUID : la valeur de backfill du tenant default.
        var match = Regex.Match(sql, "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
        match.Success.Should().BeTrue("la migration V017 doit contenir le company_id de backfill du default");
        return Normalize(match.Value);
    }

    private static string[] ReadRealmUserCompanyIds(string repoRelativePath)
    {
        var path = RepoFile(repoRelativePath);
        using var realm = JsonDocument.Parse(File.ReadAllText(path));

        return realm.RootElement.GetProperty("users").EnumerateArray()
            .Select(user =>
                user.TryGetProperty("attributes", out var attrs)
                && attrs.TryGetProperty("company_id", out var values)
                && values.ValueKind == JsonValueKind.Array
                && values.GetArrayLength() > 0
                    ? values[0].GetString()
                    : null)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => Normalize(v!))
            .ToArray();
    }

    private static string Normalize(string guid) => Guid.Parse(guid).ToString("D");

    private static string RepoFile(string repoRelativePath)
    {
        var fullPath = Path.Combine(FindRepoRoot(), repoRelativePath.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(fullPath).Should().BeTrue($"fichier introuvable : {fullPath}");
        return fullPath;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "deploy", "docker", "keycloak", "realm-export.json")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "Racine du dépôt introuvable depuis " + AppContext.BaseDirectory);
    }
}
