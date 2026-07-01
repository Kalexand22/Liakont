namespace Liakont.Host.Tests.Unit.InstanceEmail;

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.InstanceEmail;
using Liakont.Modules.FleetSupervision.Application;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

/// <summary>
/// Service console de la config email d'instance (ADR-0039) : chiffrement au save sous les purposes dédiés,
/// lit-puis-conserve (un secret vide ne remplace pas l'existant), DTO de lecture masqué (booléens <c>Has*</c>,
/// jamais de secret), et envoi d'un email de test (délégation au transport, jamais d'exception vers l'UI).
/// </summary>
public sealed class InstanceEmailConfigServiceTests
{
    private static readonly FakeSecretProtector Protector = new();

    [Fact]
    public async Task GetAsync_Without_Config_Returns_Empty_Defaults()
    {
        var service = NewService(new FakeInstanceEmailConfigStore());

        var vm = await service.GetAsync();

        vm.Form.Kind.Should().Be("SmtpBasic");
        vm.Form.Port.Should().Be(587);
        vm.Form.Enabled.Should().BeFalse();
        vm.HasSmtpPassword.Should().BeFalse();
        vm.HasOAuthClientSecret.Should().BeFalse();
        vm.HasOAuthRefreshToken.Should().BeFalse();
    }

    [Fact]
    public async Task GetAsync_Masks_Secrets_As_Has_Flags_And_Never_Exposes_Them()
    {
        var store = new FakeInstanceEmailConfigStore
        {
            Current = new InstanceEmailConfig
            {
                Kind = EmailProviderKind.SmtpBasic,
                Host = "smtp.test",
                Port = 587,
                UseStartTls = true,
                FromAddress = "from@test",
                FromName = "Nom",
                Username = "user",
                EncryptedSmtpPassword = Protector.Protect("pw", EmailSecretPurposes.SmtpPassword),
                Enabled = true,
            },
        };
        var service = NewService(store);

        var vm = await service.GetAsync();

        vm.Form.Host.Should().Be("smtp.test");
        vm.Form.Enabled.Should().BeTrue();
        vm.HasSmtpPassword.Should().BeTrue("un mot de passe est enregistré (chiffré)");
        vm.HasOAuthClientSecret.Should().BeFalse();

        // Le secret n'est JAMAIS reversé au navigateur : le champ reste vide (INV-EMAIL-CFG-01).
        vm.Form.SmtpPassword.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAsync_Encrypts_Each_Secret_Under_Its_Dedicated_Purpose()
    {
        var store = new FakeInstanceEmailConfigStore();
        var service = NewService(store);

        await service.SaveAsync(OAuthInput(clientSecret: "cs", refreshToken: "rt"));

        store.LastUpserted.Should().NotBeNull();
        store.LastUpserted!.EncryptedOAuthClientSecret.Should().Be(
            Protector.Protect("cs", EmailSecretPurposes.OAuthClientSecret),
            "le client_secret est chiffré sous son purpose dédié");
        store.LastUpserted.EncryptedOAuthRefreshToken.Should().Be(
            Protector.Protect("rt", EmailSecretPurposes.OAuthRefreshToken));

        // client_id / tenant_id = non-secrets → stockés EN CLAIR (jamais chiffrés).
        store.LastUpserted.OAuthClientId.Should().Be("client-abc");
        store.LastUpserted.OAuthTenantId.Should().Be("tenant-xyz");
    }

    [Fact]
    public async Task SaveAsync_With_Blank_Secret_Preserves_The_Existing_Ciphertext()
    {
        var existingCipher = Protector.Protect("old-pw", EmailSecretPurposes.SmtpPassword);
        var store = new FakeInstanceEmailConfigStore
        {
            Current = SmtpBasicConfig(encryptedPassword: existingCipher),
        };
        var service = NewService(store);

        // L'opérateur ré-enregistre sans re-saisir le mot de passe (champ masqué laissé vide).
        await service.SaveAsync(SmtpBasicInput(smtpPassword: null, host: "new.smtp.test"));

        store.LastUpserted!.Host.Should().Be("new.smtp.test", "les champs non-secrets sont bien mis à jour");
        store.LastUpserted.EncryptedSmtpPassword.Should().Be(
            existingCipher, "un secret vide au save CONSERVE le ciphertext existant (ADR-0039 §5, lit-puis-conserve)");
    }

    [Fact]
    public async Task SaveAsync_With_A_New_Secret_Rotates_The_Ciphertext()
    {
        var store = new FakeInstanceEmailConfigStore
        {
            Current = SmtpBasicConfig(encryptedPassword: Protector.Protect("old-pw", EmailSecretPurposes.SmtpPassword)),
        };
        var service = NewService(store);

        await service.SaveAsync(SmtpBasicInput(smtpPassword: "new-pw", host: "smtp.test"));

        store.LastUpserted!.EncryptedSmtpPassword.Should().Be(
            Protector.Protect("new-pw", EmailSecretPurposes.SmtpPassword), "un secret non vide est ré-chiffré (rotation)");
    }

    [Fact]
    public async Task SaveAsync_Rejects_An_Unknown_Provider_Kind()
    {
        var service = NewService(new FakeInstanceEmailConfigStore());

        var act = async () => await service.SaveAsync(SmtpBasicInput(smtpPassword: "pw", host: "smtp.test") with { Kind = "Carrier Pigeon" });

        await act.Should().ThrowAsync<ArgumentException>("un kind hors liste fermée est rejeté, jamais deviné");
    }

    [Fact]
    public async Task SendTestAsync_When_Not_Enabled_Reports_Failure_Without_Sending()
    {
        var transport = new RecordingEmailTransport();
        var store = new FakeInstanceEmailConfigStore { Current = SmtpBasicConfig(enabled: false) };
        var service = NewService(store, transport);

        var result = await service.SendTestAsync("ops@test");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("pas activée");
        transport.Sent.Should().BeEmpty("rien n'est envoyé si la configuration n'est pas activée");
    }

    [Fact]
    public async Task SendTestAsync_When_Oauth_Config_Is_Incomplete_Reports_Failure_Without_Sending()
    {
        // Config OAuth activée mais SANS refresh_token : le transport la traiterait comme non configurée (no-op).
        // Le service DOIT refuser le test au lieu d'annoncer un faux « envoyé » (faux-vert de la vérification).
        var transport = new RecordingEmailTransport();
        var store = new FakeInstanceEmailConfigStore
        {
            Current = new InstanceEmailConfig
            {
                Kind = EmailProviderKind.GoogleOAuth2,
                Host = "smtp.gmail.com",
                Port = 587,
                UseStartTls = true,
                FromAddress = "from@test",
                FromName = "Nom",
                Username = "user@test",
                OAuthClientId = "client-abc",
                EncryptedOAuthClientSecret = "CIPHER-cs",
                EncryptedOAuthRefreshToken = null,
                Enabled = true,
            },
        };
        var service = NewService(store, transport);

        var result = await service.SendTestAsync("ops@test");

        result.Success.Should().BeFalse("une config OAuth incomplète ne peut pas être testée — jamais un faux succès");
        result.Message.Should().Contain("incomplète");
        transport.Sent.Should().BeEmpty("rien n'est envoyé tant que la config OAuth n'est pas complète");
    }

    [Fact]
    public async Task SendTestAsync_When_Enabled_Delegates_To_The_Transport()
    {
        var transport = new RecordingEmailTransport();
        var store = new FakeInstanceEmailConfigStore { Current = SmtpBasicConfig(enabled: true) };
        var service = NewService(store, transport);

        var result = await service.SendTestAsync("ops@test");

        result.Success.Should().BeTrue();
        transport.Sent.Should().ContainSingle().Which.Recipient.Should().Be("ops@test");
    }

    [Fact]
    public async Task SendTestAsync_When_The_Transport_Throws_Reports_Failure_Without_Rethrowing()
    {
        var transport = new RecordingEmailTransport { ThrowOnSend = true };
        var store = new FakeInstanceEmailConfigStore { Current = SmtpBasicConfig(enabled: true) };
        var service = NewService(store, transport);

        var result = await service.SendTestAsync("ops@test");

        result.Success.Should().BeFalse("l'échec est un résultat, jamais une exception vers l'UI");
        result.Message.Should().Contain("échoué");
    }

    [Fact]
    public async Task SendTestAsync_With_Blank_Recipient_Reports_Failure()
    {
        var service = NewService(new FakeInstanceEmailConfigStore { Current = SmtpBasicConfig(enabled: true) });

        var result = await service.SendTestAsync("   ");

        result.Success.Should().BeFalse();
    }

    [Fact]
    public void The_Input_ToString_Does_Not_Leak_Clear_Secrets()
    {
        // InstanceEmailConfigInput transporte les secrets EN CLAIR : son ToString() (record) doit les redacter
        // pour ne jamais fuir par un log/interpolation accidentel (CLAUDE.md n°10/18).
        var input = OAuthInput(clientSecret: "cs-secret", refreshToken: "rt-secret") with { SmtpPassword = "pw-secret" };

        input.ToString().Should()
            .NotContain("cs-secret").And.NotContain("rt-secret").And.NotContain("pw-secret");
    }

    private static InstanceEmailConfig SmtpBasicConfig(bool enabled = true, string? encryptedPassword = null) => new()
    {
        Kind = EmailProviderKind.SmtpBasic,
        Host = "smtp.test",
        Port = 587,
        UseStartTls = true,
        FromAddress = "from@test",
        FromName = "Nom",
        Username = "user",
        EncryptedSmtpPassword = encryptedPassword,
        Enabled = enabled,
    };

    private static InstanceEmailConfigInput SmtpBasicInput(string? smtpPassword, string host) => new()
    {
        Kind = "SmtpBasic",
        Host = host,
        Port = 587,
        UseStartTls = true,
        FromAddress = "from@test",
        FromName = "Nom",
        Username = "user",
        SmtpPassword = smtpPassword,
        Enabled = true,
    };

    private static InstanceEmailConfigInput OAuthInput(string clientSecret, string refreshToken) => new()
    {
        Kind = "GoogleOAuth2",
        Host = "smtp.gmail.com",
        Port = 587,
        UseStartTls = true,
        FromAddress = "from@test",
        FromName = "Nom",
        Username = "user@test",
        OAuthClientId = "client-abc",
        OAuthTenantId = "tenant-xyz",
        OAuthClientSecret = clientSecret,
        OAuthRefreshToken = refreshToken,
        Enabled = true,
    };

    private static InstanceEmailConfigService NewService(
        FakeInstanceEmailConfigStore store, RecordingEmailTransport? transport = null) =>
        new(store, Protector, transport ?? new RecordingEmailTransport(), NullLogger<InstanceEmailConfigService>.Instance);
}
