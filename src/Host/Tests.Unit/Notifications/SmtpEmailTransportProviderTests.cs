namespace Liakont.Host.Tests.Unit.Notifications;

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.Configuration;
using Liakont.Host.InstanceEmail;
using Liakont.Host.Notifications;
using Liakont.Host.Tests.Unit.InstanceEmail;
using Liakont.Modules.FleetSupervision.Application;
using Liakont.Modules.TenantSettings.Application;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// Résolution de la configuration effective du transport provider-aware (ADR-0039 §6) : la ligne DB
/// <c>Enabled</c> et complète est AUTORITAIRE (SMTP basic / OAuth2, secrets DÉCHIFFRÉS avec le bon purpose) ;
/// à défaut, repli <c>appsettings</c> ; à défaut, no-op. Testé via <c>ResolveAsync</c> (sans réseau) : l'envoi
/// MailKit réel relève d'une vérification de déploiement (ADR-0018). Anti-faux-vert sur la précédence, le
/// choix du kind et la symétrie de purpose au déchiffrement.
/// </summary>
public sealed class SmtpEmailTransportProviderTests
{
    private static readonly FakeSecretProtector Protector = new();

    private static SmtpOptions ConfiguredAppsettings() => new()
    {
        Enabled = true,
        Host = "appsettings.smtp.test",
        Port = 25,
        FromAddress = "bootstrap@liakont.test",
    };

    [Fact]
    public async Task Enabled_Db_Config_Is_Authoritative_Over_Appsettings()
    {
        var store = new FakeInstanceEmailConfigStore
        {
            Current = SmtpBasicConfig(host: "db.smtp.test", password: "s3cret", enabled: true),
        };
        var transport = NewTransport(ConfiguredAppsettings(), store);

        var resolved = await transport.ResolveAsync(default);

        resolved.Should().NotBeNull();
        resolved!.Kind.Should().Be(EmailProviderKind.SmtpBasic);
        resolved.Shape.Host.Should().Be("db.smtp.test", "la ligne DB active l'emporte sur appsettings (ADR-0039 §6)");
        resolved.Password.Should().Be("s3cret", "le mot de passe SMTP est déchiffré sous son purpose dédié");
        resolved.OAuth.Should().BeNull();
    }

    [Fact]
    public async Task Disabled_Db_Config_Falls_Back_To_Appsettings()
    {
        var store = new FakeInstanceEmailConfigStore
        {
            Current = SmtpBasicConfig(host: "db.smtp.test", password: "s3cret", enabled: false),
        };
        var transport = NewTransport(ConfiguredAppsettings(), store);

        var resolved = await transport.ResolveAsync(default);

        resolved.Should().NotBeNull();
        resolved!.Shape.Host.Should().Be("appsettings.smtp.test", "une ligne DB désactivée n'est PAS autoritaire — repli bootstrap");
        resolved.Kind.Should().Be(EmailProviderKind.SmtpBasic);
    }

    [Fact]
    public async Task No_Db_Config_And_Disabled_Appsettings_Resolves_To_NoOp()
    {
        var transport = NewTransport(new SmtpOptions { Enabled = false }, new FakeInstanceEmailConfigStore());

        var resolved = await transport.ResolveAsync(default);

        resolved.Should().BeNull("ni base ni appsettings configurés → no-op (jamais une exception)");
    }

    [Fact]
    public async Task Enabled_OAuth_Config_Carries_The_Decrypted_Token_Request()
    {
        var store = new FakeInstanceEmailConfigStore
        {
            Current = new InstanceEmailConfig
            {
                Kind = EmailProviderKind.GoogleOAuth2,
                Host = "smtp.gmail.com",
                Port = 587,
                UseStartTls = true,
                FromAddress = "conformite@exemple.fr",
                FromName = "Conformité",
                Username = "conformite@exemple.fr",
                OAuthClientId = "client-abc",
                OAuthTenantId = null,
                EncryptedOAuthClientSecret = Protector.Protect("cs-plain", EmailSecretPurposes.OAuthClientSecret),
                EncryptedOAuthRefreshToken = Protector.Protect("rt-plain", EmailSecretPurposes.OAuthRefreshToken),
                Enabled = true,
            },
        };
        var transport = NewTransport(new SmtpOptions { Enabled = false }, store);

        var resolved = await transport.ResolveAsync(default);

        resolved.Should().NotBeNull();
        resolved!.Kind.Should().Be(EmailProviderKind.GoogleOAuth2);
        resolved.Password.Should().BeNull("les kinds OAuth n'utilisent pas de mot de passe SMTP");
        resolved.OAuth.Should().NotBeNull();
        resolved.OAuth!.ClientId.Should().Be("client-abc");
        resolved.OAuth.ClientSecret.Should().Be("cs-plain", "le client_secret est déchiffré sous son purpose dédié");
        resolved.OAuth.RefreshToken.Should().Be("rt-plain", "le refresh_token est déchiffré sous son purpose dédié");
    }

    [Fact]
    public async Task Enabled_OAuth_Config_Missing_A_Secret_Is_Treated_As_Not_Configured()
    {
        // Un kind OAuth activé mais SANS refresh_token n'est pas exploitable → repli/no-op (jamais une auth
        // incomplète), exactement comme un SMTP basic partiellement configuré.
        var store = new FakeInstanceEmailConfigStore
        {
            Current = new InstanceEmailConfig
            {
                Kind = EmailProviderKind.GoogleOAuth2,
                Host = "smtp.gmail.com",
                Port = 587,
                UseStartTls = true,
                FromAddress = "conformite@exemple.fr",
                FromName = "Conformité",
                Username = "conformite@exemple.fr",
                OAuthClientId = "client-abc",
                EncryptedOAuthClientSecret = Protector.Protect("cs-plain", EmailSecretPurposes.OAuthClientSecret),
                EncryptedOAuthRefreshToken = null,
                Enabled = true,
            },
        };
        var transport = NewTransport(new SmtpOptions { Enabled = false }, store);

        var resolved = await transport.ResolveAsync(default);

        resolved.Should().BeNull("un OAuth activé mais sans refresh_token n'est pas configuré → no-op");
    }

    [Fact]
    public async Task Resolved_Config_ToString_Does_Not_Leak_The_Decrypted_Password()
    {
        var store = new FakeInstanceEmailConfigStore
        {
            Current = SmtpBasicConfig(host: "db.smtp.test", password: "s3cret", enabled: true),
        };
        var transport = NewTransport(ConfiguredAppsettings(), store);

        var resolved = await transport.ResolveAsync(default);

        resolved!.ToString().Should().NotContain("s3cret", "le mot de passe déchiffré ne doit jamais fuir par ToString()");
    }

    [Fact]
    public async Task IsConfiguredAsync_Is_True_With_A_Db_Only_Config()
    {
        // LE bug de recette BUG-31 : config d'instance EN BASE seule (Gmail), appsettings vides — la
        // disponibilité d'envoi doit être VRAIE (l'invitation part au lieu d'afficher le mot de passe).
        var store = new FakeInstanceEmailConfigStore
        {
            Current = SmtpBasicConfig(host: "db.smtp.test", password: "s3cret", enabled: true),
        };
        var transport = NewTransport(new SmtpOptions { Enabled = false }, store);

        (await transport.IsConfiguredAsync()).Should().BeTrue("la config d'instance en base suffit (BUG-31)");
    }

    [Fact]
    public async Task IsConfiguredAsync_Is_True_With_Appsettings_Only()
    {
        var transport = NewTransport(ConfiguredAppsettings(), new FakeInstanceEmailConfigStore());

        (await transport.IsConfiguredAsync()).Should().BeTrue("le repli appsettings reste une config d'envoi valable");
    }

    [Fact]
    public async Task IsConfiguredAsync_Is_False_Without_Any_Config()
    {
        var transport = NewTransport(new SmtpOptions { Enabled = false }, new FakeInstanceEmailConfigStore());

        (await transport.IsConfiguredAsync()).Should().BeFalse("ni base ni appsettings → le mot de passe est remis à l'opérateur");
    }

    [Fact]
    public async Task IsConfiguredAsync_Never_Decrypts_Secrets()
    {
        // La sonde de disponibilité est consommée par le provisioning d'utilisateur (invitation / reset) :
        // une clé de chiffrement invalide (ciphertext d'un autre hôte, clé tournée) doit faire échouer
        // l'ENVOI — rattrapé par l'appelant — JAMAIS la sonde elle-même (review BUG-31).
        var store = new FakeInstanceEmailConfigStore
        {
            Current = SmtpBasicConfig(host: "db.smtp.test", password: "s3cret", enabled: true),
        };
        var transport = new SmtpEmailTransport(
            Options.Create(new SmtpOptions { Enabled = false }),
            Options.Create(new BrandingOptions()),
            store,
            new ThrowingSecretProtector(),
            new FakeEmailOAuthTokenProvider(),
            NullLogger<SmtpEmailTransport>.Instance);

        (await transport.IsConfiguredAsync()).Should().BeTrue("la disponibilité se sonde par null-checks, sans Unprotect");
    }

    private static InstanceEmailConfig SmtpBasicConfig(string host, string password, bool enabled) => new()
    {
        Kind = EmailProviderKind.SmtpBasic,
        Host = host,
        Port = 587,
        UseStartTls = true,
        FromAddress = "supervision@liakont.test",
        FromName = "Supervision",
        Username = "supervision@liakont.test",
        EncryptedSmtpPassword = Protector.Protect(password, EmailSecretPurposes.SmtpPassword),
        Enabled = enabled,
    };

    private static SmtpEmailTransport NewTransport(SmtpOptions appsettings, FakeInstanceEmailConfigStore store) =>
        new(
            Options.Create(appsettings),
            Options.Create(new BrandingOptions()),
            store,
            Protector,
            new FakeEmailOAuthTokenProvider(),
            NullLogger<SmtpEmailTransport>.Instance);

    /// <summary>Lève sur TOUT déchiffrement — prouve que la sonde de disponibilité n'en fait aucun.</summary>
    private sealed class ThrowingSecretProtector : ISecretProtector
    {
        public string Protect(string plaintext) => throw new InvalidOperationException("aucun chiffrement attendu ici");

        public string Unprotect(string protectedValue) => throw new InvalidOperationException("la sonde ne déchiffre jamais");

        public string Protect(string plaintext, string purpose) => throw new InvalidOperationException("aucun chiffrement attendu ici");

        public string Unprotect(string protectedValue, string purpose) => throw new InvalidOperationException("la sonde ne déchiffre jamais");
    }
}
