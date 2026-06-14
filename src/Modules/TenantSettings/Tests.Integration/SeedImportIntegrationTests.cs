namespace Liakont.Modules.TenantSettings.Tests.Integration;

using Dapper;
using FluentAssertions;
using Liakont.Modules.TenantSettings.Contracts.Commands;
using Liakont.Modules.TenantSettings.Infrastructure.Handlers.Commands;
using Liakont.Modules.TenantSettings.Tests.Integration.Fixtures;
using Liakont.Modules.TvaMapping.Contracts.Commands;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Common.Infrastructure.DataIsolation;
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
            var handler = new ImportTenantSeedHandler(harness.UowFactory, harness.CompanyFilter, harness.ActorAccessor, harness.Queries, harness.Journal, harness.Sender);

            var result = await handler.Handle(new ImportTenantSeedCommand { SeedDirectoryPath = dir }, CancellationToken.None);

            // Aucun mapping-tva.json dans ce dossier → import de mapping non déclenché (drapeau faux, sender non sollicité).
            result.TvaMappingImported.Should().BeFalse();
            harness.Sender.LastRequest.Should().BeNull();

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
            var handler = new ImportTenantSeedHandler(harness.UowFactory, harness.CompanyFilter, harness.ActorAccessor, harness.Queries, harness.Journal, harness.Sender);
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

    [Fact]
    public async Task Import_With_Mapping_Seed_Dispatches_Mapping_Import_Command()
    {
        const string mappingJson = """
            {
              "mappingVersion": "tenant-seed-exemple-v1",
              "validatedBy": "Table d'exemple — tests",
              "validatedDate": null,
              "defaultBehavior": "Block",
              "rules": [
                { "sourceRegimeCode": "NORMAL", "label": "Normal", "part": "Adjudication", "category": "S", "rateMode": "Fixed", "rateValue": 20 }
              ]
            }
            """;

        var dir = CreateSeedDir();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "tenant-profile.json"), ProfileJson);
            var mappingPath = Path.Combine(dir, "mapping-tva.json");
            await File.WriteAllTextAsync(mappingPath, mappingJson);

            var harness = new TenantSettingsHarness(_fixture, Guid.NewGuid(), Guid.NewGuid());
            var handler = new ImportTenantSeedHandler(harness.UowFactory, harness.CompanyFilter, harness.ActorAccessor, harness.Queries, harness.Journal, harness.Sender);

            var result = await handler.Handle(new ImportTenantSeedCommand { SeedDirectoryPath = dir }, CancellationToken.None);

            // Le point d'entrée OPS03 (import de seed) dispatche bien l'import de mapping TVA, avec le chemin
            // du fichier présent dans le dossier de seed (item FIX01b), et reporte le résultat dans le drapeau.
            result.TvaMappingImported.Should().BeTrue();
            var dispatched = harness.Sender.LastRequest.Should().BeOfType<ImportMappingTableSeedCommand>().Subject;
            dispatched.SeedFilePath.Should().Be(mappingPath);
            dispatched.CompanyId.Should().Be(harness.CompanyId,
                "le companyId résolu est propagé à l'import de table (plus de re-déduction ambiante au boot — FIX203a).");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Import_With_Explicit_CompanyId_Bypasses_The_Actor_CompanyFilter()
    {
        // Chemin amorçage / endpoint admin : aucun actor du tenant cible n'est établi. Un companyId
        // explicite DOIT être utilisé directement (le filtre actor n'est jamais consulté) — sinon le
        // 1er profil ne pourrait pas être créé (FIX01a).
        var dir = CreateSeedDir();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "tenant-profile.json"), ProfileJson);

            var companyId = Guid.NewGuid();
            var harness = new TenantSettingsHarness(_fixture, companyId, Guid.NewGuid());
            var handler = new ImportTenantSeedHandler(harness.UowFactory, new ThrowingCompanyFilter(), harness.ActorAccessor, harness.Queries, harness.Journal, harness.Sender);

            var result = await handler.Handle(
                new ImportTenantSeedCommand { SeedDirectoryPath = dir, CompanyId = companyId },
                CancellationToken.None);

            result.ProfileImported.Should().BeTrue();
            (await harness.Queries.GetTenantProfile(companyId))!.Siren.Should().Be("123456782");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Import_With_Explicit_CompanyId_Threads_It_To_The_Mapping_Dispatch()
    {
        // Boot à froid (FIX203a) : avec un companyId explicite et SANS contexte de société ambiant (filtre
        // qui échoue), l'import de mapping TVA dispatché porte ce MÊME companyId explicite — donc le handler
        // de mapping n'a plus à re-déduire la société (qui manquait au démarrage, laissant la table jamais
        // amorcée). Le filtre qui échoue prouve qu'aucune résolution ambiante n'intervient.
        const string mappingJson = """
            {
              "mappingVersion": "tenant-seed-exemple-v1",
              "validatedBy": "Table d'exemple — tests",
              "validatedDate": null,
              "defaultBehavior": "Block",
              "rules": [
                { "sourceRegimeCode": "NORMAL", "label": "Normal", "part": "Adjudication", "category": "S", "rateMode": "Fixed", "rateValue": 20 }
              ]
            }
            """;

        var dir = CreateSeedDir();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "tenant-profile.json"), ProfileJson);
            var mappingPath = Path.Combine(dir, "mapping-tva.json");
            await File.WriteAllTextAsync(mappingPath, mappingJson);

            var companyId = Guid.NewGuid();
            var harness = new TenantSettingsHarness(_fixture, companyId, Guid.NewGuid());
            var handler = new ImportTenantSeedHandler(harness.UowFactory, new ThrowingCompanyFilter(), harness.ActorAccessor, harness.Queries, harness.Journal, harness.Sender);

            var result = await handler.Handle(
                new ImportTenantSeedCommand { SeedDirectoryPath = dir, CompanyId = companyId },
                CancellationToken.None);

            result.TvaMappingImported.Should().BeTrue();
            var dispatched = harness.Sender.LastRequest.Should().BeOfType<ImportMappingTableSeedCommand>().Subject;
            dispatched.SeedFilePath.Should().Be(mappingPath);
            dispatched.CompanyId.Should().Be(companyId,
                "le companyId explicite de l'amorçage est propagé à l'import de table (aucune dépendance au contexte ambiant).");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Import_With_CompanyId_Conflicting_With_Actor_Is_Rejected_When_The_Tenant_Is_Already_Configured()
    {
        // Garde anti-injection (P2) : un companyId explicite qui CONTREDIT la société d'un acteur de
        // tenant présent est refusé dès que le tenant est DÉJÀ paramétré (un profil existe). La
        // précondition « profil existant » est AMORCÉE explicitement — la base de la collection est
        // partagée, le refus ne doit JAMAIS dépendre des résidus d'un autre test (l'état create-only
        // SANS profil est, lui, le chemin honoré — couvert par TenantSettingsCompanyOverrideGuardTests).
        var dir = CreateSeedDir();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "tenant-profile.json"), ProfileJson);

            var actorCompanyId = Guid.NewGuid();
            var harness = new TenantSettingsHarness(_fixture, actorCompanyId, Guid.NewGuid());
            await InsertExistingProfileAsync(harness, Guid.NewGuid());

            var handler = new ImportTenantSeedHandler(harness.UowFactory, harness.CompanyFilter, harness.ActorAccessor, harness.Queries, harness.Journal, harness.Sender);

            var act = async () => await handler.Handle(
                new ImportTenantSeedCommand { SeedDirectoryPath = dir, CompanyId = Guid.NewGuid() },
                CancellationToken.None);

            await act.Should().ThrowAsync<ConflictException>();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>Amorce un profil EXISTANT (précondition du refus de la garde — tenant déjà paramétré).</summary>
    private static async Task InsertExistingProfileAsync(TenantSettingsHarness harness, Guid companyId)
    {
        using var conn = await harness.ConnectionFactory.OpenAsync();
        await conn.ExecuteAsync(
            """
            INSERT INTO tenantsettings.tenant_profiles
                (company_id, siren, raison_sociale, address_street, address_postal_code, address_city, address_country)
            VALUES (@CompanyId, '999999990', 'Tenant déjà paramétré', '1 rue Occupée', '35000', 'Rennes', 'FR')
            """,
            new { CompanyId = companyId });
    }

    private static string CreateSeedDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "liakont-seed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Filtre de société qui échoue : prouve qu'un companyId explicite ne consulte jamais l'actor.</summary>
    private sealed class ThrowingCompanyFilter : ICompanyFilter
    {
        public Guid GetRequiredCompanyId() =>
            throw new InvalidOperationException("Le filtre de société ne doit pas être consulté quand un companyId explicite est fourni.");
    }
}
