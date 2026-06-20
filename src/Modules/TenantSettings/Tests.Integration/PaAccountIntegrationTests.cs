namespace Liakont.Modules.TenantSettings.Tests.Integration;

using Dapper;
using FluentAssertions;
using Liakont.Modules.TenantSettings.Application;
using Liakont.Modules.TenantSettings.Contracts.Commands;
using Liakont.Modules.TenantSettings.Infrastructure;
using Liakont.Modules.TenantSettings.Infrastructure.Handlers.Commands;
using Liakont.Modules.TenantSettings.Tests.Integration.Fixtures;
using Stratum.Common.Abstractions.Exceptions;
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
    public async Task OAuth_Client_Id_And_Secret_Are_Encrypted_At_Rest_And_Round_Trip()
    {
        const string clientId = "client-FICTIF-0123";
        const string clientSecret = "secret-FICTIF-9876";

        var harness = new TenantSettingsHarness(_fixture, Guid.NewGuid(), Guid.NewGuid());
        var addHandler = new AddPaAccountHandler(harness.UowFactory, harness.CompanyFilter, harness.SecretProtector, harness.Journal);

        var id = await addHandler.Handle(
            new AddPaAccountCommand
            {
                PluginType = "SuperPdp",
                Environment = "Staging",
                AccountIdentifiers = "acct-1",
                ClientId = clientId,
                ClientSecret = clientSecret,
            },
            CancellationToken.None);

        // 1. Les colonnes OAuth2 ne contiennent PAS le clair, mais un texte chiffré non vide.
        string? storedClientId;
        string? storedClientSecret;
        using (var conn = await harness.ConnectionFactory.OpenAsync())
        {
            var row = await conn.QuerySingleAsync(
                "SELECT encrypted_client_id, encrypted_client_secret FROM tenantsettings.pa_accounts WHERE id = @Id",
                new { Id = id });
            storedClientId = (string?)row.encrypted_client_id;
            storedClientSecret = (string?)row.encrypted_client_secret;
        }

        storedClientId.Should().NotBeNullOrEmpty().And.NotBe(clientId).And.NotContain(clientId);
        storedClientSecret.Should().NotBeNullOrEmpty().And.NotBe(clientSecret).And.NotContain(clientSecret);

        // 2. Relecture via l'UoW : les colonnes chiffrées sont peuplées (le domaine ne voit que l'opaque).
        await using (var uow = await harness.UowFactory.BeginAsync())
        {
            var account = await uow.GetPaAccountByIdAsync(id, harness.CompanyId);
            account!.EncryptedClientId.Should().Be(storedClientId);
            account.EncryptedClientSecret.Should().Be(storedClientSecret);
        }

        // 3. Le store de secrets (consommé par le résolveur Host) déchiffre vers le clair d'origine, par purpose.
        var secretStore = new PostgresPaAccountSecretStore(harness.ConnectionFactory);
        var secrets = await secretStore.GetActiveAsync(harness.CompanyId, "SuperPdp");
        secrets.Should().NotBeNull();
        harness.SecretProtector.Unprotect(secrets!.EncryptedClientId!, PaAccountSecretPurposes.ClientId).Should().Be(clientId);
        harness.SecretProtector.Unprotect(secrets.EncryptedClientSecret!, PaAccountSecretPurposes.ClientSecret).Should().Be(clientSecret);

        // 4. Le DTO de lecture n'expose jamais le secret — seulement son existence (booléens Has*).
        var accounts = await harness.Queries.GetPaAccounts(harness.CompanyId);
        accounts.Should().ContainSingle();
        accounts[0].HasClientId.Should().BeTrue();
        accounts[0].HasClientSecret.Should().BeTrue();
        accounts[0].HasApiKey.Should().BeFalse();
    }

    [Fact]
    public async Task Technical_Account_Password_Is_Encrypted_At_Rest_And_Round_Trips_By_Purpose()
    {
        const string clientId = "piste-client-FICTIF-0123";
        const string clientSecret = "piste-secret-FICTIF-9876";
        const string technicalPassword = "tech-pwd-FICTIF-4242";

        var harness = new TenantSettingsHarness(_fixture, Guid.NewGuid(), Guid.NewGuid());
        var addHandler = new AddPaAccountHandler(harness.UowFactory, harness.CompanyFilter, harness.SecretProtector, harness.Journal);

        var id = await addHandler.Handle(
            new AddPaAccountCommand
            {
                PluginType = "ChorusPro",
                Environment = "Staging",
                AccountIdentifiers = "tech-login@tenant.fr",
                ClientId = clientId,
                ClientSecret = clientSecret,
                TechnicalPassword = technicalPassword,
            },
            CancellationToken.None);

        // 1. La colonne du mot de passe technique ne contient PAS le clair, mais un texte chiffré non vide.
        string? storedTechnicalPassword;
        using (var conn = await harness.ConnectionFactory.OpenAsync())
        {
            storedTechnicalPassword = await conn.ExecuteScalarAsync<string?>(
                "SELECT encrypted_technical_password FROM tenantsettings.pa_accounts WHERE id = @Id",
                new { Id = id });
        }

        storedTechnicalPassword.Should().NotBeNullOrEmpty()
            .And.NotBe(technicalPassword).And.NotContain(technicalPassword);

        // 2. Relecture via l'UoW : la colonne chiffrée est peuplée (le domaine ne voit que l'opaque).
        await using (var uow = await harness.UowFactory.BeginAsync())
        {
            var account = await uow.GetPaAccountByIdAsync(id, harness.CompanyId);
            account!.EncryptedTechnicalPassword.Should().Be(storedTechnicalPassword);
        }

        // 3. Le store de secrets (consommé par le résolveur Host) déchiffre vers le clair d'origine, par purpose.
        var secretStore = new PostgresPaAccountSecretStore(harness.ConnectionFactory);
        var secrets = await secretStore.GetActiveAsync(harness.CompanyId, "ChorusPro");
        secrets.Should().NotBeNull();
        harness.SecretProtector.Unprotect(secrets!.EncryptedTechnicalPassword!, PaAccountSecretPurposes.TechnicalPassword)
            .Should().Be(technicalPassword);

        // 4. Le DTO de lecture n'expose jamais le secret — seulement son existence (booléen Has*).
        var accounts = await harness.Queries.GetPaAccounts(harness.CompanyId);
        accounts.Should().ContainSingle();
        accounts[0].HasTechnicalPassword.Should().BeTrue();
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
    public async Task Add_Duplicate_Plugin_And_Environment_Throws_Conflict()
    {
        var harness = new TenantSettingsHarness(_fixture, Guid.NewGuid(), Guid.NewGuid());
        var addHandler = new AddPaAccountHandler(harness.UowFactory, harness.CompanyFilter, harness.SecretProtector, harness.Journal);

        await addHandler.Handle(
            new AddPaAccountCommand { PluginType = "Fake", Environment = "Staging", AccountIdentifiers = "{}", ApiKey = null },
            CancellationToken.None);

        var act = () => addHandler.Handle(
            new AddPaAccountCommand { PluginType = "Fake", Environment = "Staging", AccountIdentifiers = "{}", ApiKey = null },
            CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>("un doublon (tenant, plug-in, environnement) est interdit (index unique).");
    }

    [Fact]
    public async Task Update_To_Existing_Plugin_And_Environment_Throws_Conflict()
    {
        var harness = new TenantSettingsHarness(_fixture, Guid.NewGuid(), Guid.NewGuid());
        var addHandler = new AddPaAccountHandler(harness.UowFactory, harness.CompanyFilter, harness.SecretProtector, harness.Journal);
        var updateHandler = new UpdatePaAccountHandler(harness.UowFactory, harness.CompanyFilter, harness.SecretProtector, harness.Journal);

        await addHandler.Handle(
            new AddPaAccountCommand { PluginType = "Fake", Environment = "Staging", AccountIdentifiers = "{}", ApiKey = null },
            CancellationToken.None);
        var prodId = await addHandler.Handle(
            new AddPaAccountCommand { PluginType = "Fake", Environment = "Production", AccountIdentifiers = "{}", ApiKey = null },
            CancellationToken.None);

        var act = () => updateHandler.Handle(
            new UpdatePaAccountCommand { PaAccountId = prodId, Environment = "Staging", AccountIdentifiers = "{}", ApiKey = null },
            CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>("faire collisionner (tenant, plug-in, environnement) via update est interdit (index unique).");
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
