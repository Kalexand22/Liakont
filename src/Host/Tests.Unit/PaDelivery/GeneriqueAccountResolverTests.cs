namespace Liakont.Host.Tests.Unit.PaDelivery;

using System.Collections.Generic;
using FluentAssertions;
using Liakont.Host.PaDelivery;
using Liakont.Modules.TenantSettings.Application;
using Liakont.Modules.Transmission.Contracts;
using Xunit;

/// <summary>
/// Résolution d'un compte générique (F16 §6.2) : canal + cible non sensibles lus du descripteur, et mot de
/// passe SMTP par tenant DÉCHIFFRÉ via le coffre (<see cref="ISecretProtector"/>). Le descripteur ne porte
/// JAMAIS de secret en clair (CLAUDE.md n°10) ; on bloque plutôt que de livrer faux (n°3).
/// </summary>
public sealed class GeneriqueAccountResolverTests
{
    private static PaAccountDescriptor Account(Dictionary<string, string> settings) =>
        new("Generique", "tenant-1", settings);

    [Fact]
    public void Resolve_Reads_Method_And_Target()
    {
        var resolver = new GeneriqueAccountResolver(new FakeSecretProtector());

        var config = resolver.Resolve(Account(new Dictionary<string, string>
        {
            [GeneriqueAccountResolver.MethodKey] = "Email",
            [GeneriqueAccountResolver.TargetKey] = "pa@tenant.test",
        }));

        config.Method.Should().Be(DocumentDeliveryMethod.Email);
        config.Target.Should().Be("pa@tenant.test");
        config.SmtpAuth.Should().BeNull("aucun SMTP par tenant configuré → SMTP d'instance (ADR-0018)");
    }

    [Fact]
    public void Resolve_Decrypts_The_Per_Tenant_Smtp_Password_Via_The_Vault()
    {
        var protector = new FakeSecretProtector();
        var resolver = new GeneriqueAccountResolver(protector);

        var settings = new Dictionary<string, string>
        {
            [GeneriqueAccountResolver.MethodKey] = "Email",
            [GeneriqueAccountResolver.TargetKey] = "pa@tenant.test",
            [GeneriqueAccountResolver.SmtpHostKey] = "smtp.tenant.test",
            [GeneriqueAccountResolver.SmtpPortKey] = "2525",
            [GeneriqueAccountResolver.SmtpUseStartTlsKey] = "false",
            [GeneriqueAccountResolver.SmtpUsernameKey] = "tenant-user",
            [GeneriqueAccountResolver.SmtpPasswordProtectedKey] = "ENC:tenant-secret",
        };

        var config = resolver.Resolve(Account(settings));

        config.SmtpAuth.Should().NotBeNull();
        config.SmtpAuth!.Host.Should().Be("smtp.tenant.test");
        config.SmtpAuth.Port.Should().Be(2525);
        config.SmtpAuth.UseStartTls.Should().BeFalse();
        config.SmtpAuth.Username.Should().Be("tenant-user");
        config.SmtpAuth.Password.Should().Be("tenant-secret", "le mot de passe est déchiffré via le coffre");
        protector.UnprotectCalls.Should().Be(1);

        // Le descripteur n'a porté que du CHIFFRÉ — jamais le clair (CLAUDE.md n°10).
        settings[GeneriqueAccountResolver.SmtpPasswordProtectedKey].Should().Be("ENC:tenant-secret");
        settings.Values.Should().NotContain("tenant-secret");
    }

    [Fact]
    public void Resolve_Throws_When_Method_Missing_Or_Invalid()
    {
        var resolver = new GeneriqueAccountResolver(new FakeSecretProtector());

        var missing = () => resolver.Resolve(Account(new Dictionary<string, string>
        {
            [GeneriqueAccountResolver.TargetKey] = "pa@tenant.test",
        }));
        missing.Should().Throw<InvalidOperationException>();

        var invalid = () => resolver.Resolve(Account(new Dictionary<string, string>
        {
            [GeneriqueAccountResolver.MethodKey] = "Carrier-Pigeon",
            [GeneriqueAccountResolver.TargetKey] = "pa@tenant.test",
        }));
        invalid.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Resolve_Throws_When_Target_Missing()
    {
        var resolver = new GeneriqueAccountResolver(new FakeSecretProtector());

        var act = () => resolver.Resolve(Account(new Dictionary<string, string>
        {
            [GeneriqueAccountResolver.MethodKey] = "FileDeposit",
        }));

        act.Should().Throw<InvalidOperationException>();
    }

    /// <summary>Coffre FACTICE : « ENC:&lt;clair&gt; » ⇄ « &lt;clair&gt; » et compte les déchiffrements.</summary>
    private sealed class FakeSecretProtector : ISecretProtector
    {
        public int UnprotectCalls { get; private set; }

        public string Protect(string plaintext) => "ENC:" + plaintext;

        public string Protect(string plaintext, string purpose) => Protect(plaintext);

        public string Unprotect(string protectedValue)
        {
            UnprotectCalls++;
            return protectedValue.StartsWith("ENC:", StringComparison.Ordinal)
                ? protectedValue["ENC:".Length..]
                : protectedValue;
        }

        public string Unprotect(string protectedValue, string purpose) => Unprotect(protectedValue);
    }
}
