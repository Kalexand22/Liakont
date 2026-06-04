namespace Liakont.Modules.TenantSettings.Tests.Integration;

using Dapper;
using FluentAssertions;
using Liakont.Modules.TenantSettings.Contracts.Commands;
using Liakont.Modules.TenantSettings.Infrastructure.Handlers.Commands;
using Liakont.Modules.TenantSettings.Tests.Integration.Fixtures;
using Xunit;

[Collection("TenantSettingsIntegration")]
public sealed class PaAccountIntegrationTests
{
    private const string PlaintextKey = "sk_live_FICTIF_0123456789";

    private readonly TenantSettingsDatabaseFixture _fixture;

    public PaAccountIntegrationTests(TenantSettingsDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ApiKey_Is_Encrypted_At_Rest_And_Never_Exposed()
    {
        var harness = new TenantSettingsHarness(_fixture, Guid.NewGuid(), Guid.NewGuid());
        var addHandler = new AddPaAccountHandler(harness.UowFactory, harness.CompanyFilter, harness.SecretProtector, harness.Journal);

        var id = await addHandler.Handle(
            new AddPaAccountCommand
            {
                PluginType = "Fake",
                Environment = "Staging",
                AccountIdentifiers = "{}",
                ApiKey = PlaintextKey,
            },
            CancellationToken.None);

        // 1. La colonne en base ne contient PAS le clair, mais un texte chiffré non vide.
        string? stored;
        using (var conn = await harness.ConnectionFactory.OpenAsync())
        {
            stored = await conn.ExecuteScalarAsync<string?>(
                "SELECT encrypted_api_key FROM tenantsettings.pa_accounts WHERE id = @Id",
                new { Id = id });
        }

        stored.Should().NotBeNullOrEmpty();
        stored.Should().NotBe(PlaintextKey, "la clé est chiffrée au repos (INV-TENANTSETTINGS-003).");
        stored.Should().NotContain(PlaintextKey);

        // 2. Le ciphertext se déchiffre vers le clair d'origine (round-trip Data Protection).
        harness.SecretProtector.Unprotect(stored!).Should().Be(PlaintextKey);

        // 3. Le DTO de lecture n'expose jamais la clé — seulement son existence.
        var accounts = await harness.Queries.GetPaAccounts(harness.CompanyId);
        accounts.Should().ContainSingle();
        accounts[0].HasApiKey.Should().BeTrue();
        accounts[0].IsActive.Should().BeTrue();
        accounts[0].PluginType.Should().Be("Fake");

        // 4. Mutation journalisée avec l'identité opérateur.
        harness.ActivityLogger.Entries.Should().Contain(e =>
            e.EntityType == "PaAccount" && e.ActivityType == "created" && e.ActorId == harness.UserId.ToString());
    }

    [Fact]
    public async Task Add_Without_Key_Leaves_HasApiKey_False()
    {
        var harness = new TenantSettingsHarness(_fixture, Guid.NewGuid(), Guid.NewGuid());
        var addHandler = new AddPaAccountHandler(harness.UowFactory, harness.CompanyFilter, harness.SecretProtector, harness.Journal);

        await addHandler.Handle(
            new AddPaAccountCommand { PluginType = "Fake", Environment = "Production", AccountIdentifiers = "{}", ApiKey = null },
            CancellationToken.None);

        var accounts = await harness.Queries.GetPaAccounts(harness.CompanyId);
        accounts.Should().ContainSingle();
        accounts[0].HasApiKey.Should().BeFalse();
    }

    [Fact]
    public async Task Deactivate_Marks_Account_Inactive()
    {
        var harness = new TenantSettingsHarness(_fixture, Guid.NewGuid(), Guid.NewGuid());
        var addHandler = new AddPaAccountHandler(harness.UowFactory, harness.CompanyFilter, harness.SecretProtector, harness.Journal);
        var deactivateHandler = new DeactivatePaAccountHandler(harness.UowFactory, harness.CompanyFilter, harness.Journal);

        var id = await addHandler.Handle(
            new AddPaAccountCommand { PluginType = "Fake", Environment = "Staging", AccountIdentifiers = "{}", ApiKey = PlaintextKey },
            CancellationToken.None);

        await deactivateHandler.Handle(new DeactivatePaAccountCommand { PaAccountId = id }, CancellationToken.None);

        var accounts = await harness.Queries.GetPaAccounts(harness.CompanyId);
        accounts[0].IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Update_Rotates_Key_When_Provided()
    {
        var harness = new TenantSettingsHarness(_fixture, Guid.NewGuid(), Guid.NewGuid());
        var addHandler = new AddPaAccountHandler(harness.UowFactory, harness.CompanyFilter, harness.SecretProtector, harness.Journal);
        var updateHandler = new UpdatePaAccountHandler(harness.UowFactory, harness.CompanyFilter, harness.SecretProtector, harness.Journal);

        var id = await addHandler.Handle(
            new AddPaAccountCommand { PluginType = "Fake", Environment = "Staging", AccountIdentifiers = "{}", ApiKey = PlaintextKey },
            CancellationToken.None);

        const string rotated = "sk_live_FICTIF_ROTATED_99";
        await updateHandler.Handle(
            new UpdatePaAccountCommand { PaAccountId = id, Environment = "Production", AccountIdentifiers = "{\"x\":1}", ApiKey = rotated },
            CancellationToken.None);

        string? stored;
        using (var conn = await harness.ConnectionFactory.OpenAsync())
        {
            stored = await conn.ExecuteScalarAsync<string?>(
                "SELECT encrypted_api_key FROM tenantsettings.pa_accounts WHERE id = @Id",
                new { Id = id });
        }

        harness.SecretProtector.Unprotect(stored!).Should().Be(rotated);
        var accounts = await harness.Queries.GetPaAccounts(harness.CompanyId);
        accounts[0].Environment.Should().Be("Production");
    }
}
