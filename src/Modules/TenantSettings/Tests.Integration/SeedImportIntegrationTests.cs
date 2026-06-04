namespace Liakont.Modules.TenantSettings.Tests.Integration;

using FluentAssertions;
using Liakont.Modules.TenantSettings.Contracts.Commands;
using Liakont.Modules.TenantSettings.Infrastructure.Handlers.Commands;
using Liakont.Modules.TenantSettings.Tests.Integration.Fixtures;
using Xunit;

[Collection("TenantSettingsIntegration")]
public sealed class SeedImportIntegrationTests
{
    private const string ProfileJson = """
        {
          "siren": "123456782",
          "raisonSociale": "Société Fictive de Démonstration",
          "address": { "street": "1 rue de l'Exemple", "postalCode": "35000", "city": "Rennes", "country": "FR" },
          "contactEmailAlerte": "alertes@exemple.test",
          "fiscal": { "vatOnDebits": null, "operationCategory": null, "reportingFrequency": null },
          "schedule": { "hours": ["03:00"], "catchUpOnStart": true },
          "thresholds": { "agentSilentHours": 12, "alertTenantContact": true }
        }
        """;

    private const string PaAccountsJson = """
        [
          { "pluginType": "Fake", "environment": "Staging", "accountIdentifiers": "{}", "apiKey": "${PA_API_KEY_FAKE_STAGING}" }
        ]
        """;

    private readonly TenantSettingsDatabaseFixture _fixture;

    public SeedImportIntegrationTests(TenantSettingsDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Import_Loads_Profile_Fiscal_Schedule_Thresholds_And_PaAccounts_Without_Secret()
    {
        var dir = CreateSeedDir();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "tenant-profile.json"), ProfileJson);
            await File.WriteAllTextAsync(Path.Combine(dir, "pa-accounts.json"), PaAccountsJson);

            var harness = new TenantSettingsHarness(_fixture, Guid.NewGuid(), Guid.NewGuid());
            var handler = new ImportTenantSeedHandler(harness.UowFactory, harness.CompanyFilter, harness.Journal);

            var result = await handler.Handle(new ImportTenantSeedCommand { SeedDirectoryPath = dir }, CancellationToken.None);

            result.ProfileImported.Should().BeTrue();
            result.FiscalImported.Should().BeTrue();
            result.ScheduleImported.Should().BeTrue();
            result.ThresholdsImported.Should().BeTrue();
            result.PaAccountsImported.Should().Be(1);
            result.Warnings.Should().ContainSingle(w => w.Contains("non importée", StringComparison.Ordinal));

            (await harness.Queries.GetTenantProfile(harness.CompanyId))!.Siren.Should().Be("123456782");

            var fiscal = await harness.Queries.GetFiscalSettings(harness.CompanyId);
            fiscal!.VatOnDebits.Should().BeNull("le seed laisse le fiscal en attente (jamais deviné).");

            var thresholds = await harness.Queries.GetAlertThresholds(harness.CompanyId);
            thresholds!.AgentSilentHours.Should().Be(12);
            thresholds.AlertTenantContact.Should().BeTrue();

            // La clé API n'est jamais importée (INV-TENANTSETTINGS-007).
            var accounts = await harness.Queries.GetPaAccounts(harness.CompanyId);
            accounts.Should().ContainSingle();
            accounts[0].HasApiKey.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Import_Is_Idempotent()
    {
        var dir = CreateSeedDir();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "tenant-profile.json"), ProfileJson);
            await File.WriteAllTextAsync(Path.Combine(dir, "pa-accounts.json"), PaAccountsJson);

            var harness = new TenantSettingsHarness(_fixture, Guid.NewGuid(), Guid.NewGuid());
            var handler = new ImportTenantSeedHandler(harness.UowFactory, harness.CompanyFilter, harness.Journal);
            var command = new ImportTenantSeedCommand { SeedDirectoryPath = dir };

            await handler.Handle(command, CancellationToken.None);
            await handler.Handle(command, CancellationToken.None);

            // Rejouable : un seul profil, un seul compte PA (pas de doublon).
            var accounts = await harness.Queries.GetPaAccounts(harness.CompanyId);
            accounts.Should().ContainSingle();
            (await harness.Queries.GetTenantProfile(harness.CompanyId))!.Siren.Should().Be("123456782");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string CreateSeedDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "liakont-seed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
