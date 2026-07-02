namespace Liakont.Host.Tests.Unit.Notifications;

using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.Configuration;
using Liakont.Host.Notifications;
using Liakont.Host.Tests.Unit.InstanceEmail;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;
using Xunit;

/// <summary>
/// Couvre la composition du message (sans connexion réseau) et le mode non configuré du transport SMTP
/// (ADR-0018, SUP03). Inclut le branding d'instance (BRD01, marque grise) : expéditeur dérivé du branding
/// quand il est renseigné, pied de page brandé. L'envoi SMTP réel relève d'une vérification de déploiement.
/// </summary>
public sealed class SmtpEmailTransportTests
{
    private static SmtpOptions ConfiguredOptions() => new()
    {
        Enabled = true,
        Host = "smtp.example.test",
        Port = 587,
        FromAddress = "supervision@liakont.test",
        FromName = "Liakont Supervision",
    };

    /// <summary>Branding par défaut (marque « Liakont », mention propulsé) — expéditeur email non surchargé.</summary>
    private static BrandingOptions DefaultBranding() => new();

    [Fact]
    public void BuildMessage_Sets_From_To_Subject_And_Body()
    {
        var message = SmtpEmailTransport.BuildMessage(ConfiguredOptions(), DefaultBranding(), "ops@liakont.test", "Sujet", "Corps du message.");

        var from = message.From.Mailboxes.Single();
        from.Address.Should().Be("supervision@liakont.test");
        from.Name.Should().Be("Liakont Supervision");
        message.To.Mailboxes.Single().Address.Should().Be("ops@liakont.test");
        message.Subject.Should().Be("Sujet");
        message.TextBody.Should().StartWith("Corps du message.", "le corps d'origine précède le pied de page brandé");
    }

    [Fact]
    public void BuildMessage_Preserves_French_Accents_In_Utf8()
    {
        const string subject = "[Liakont] Alerte critique — tenant acme";
        const string body = "L'agent ne répond plus depuis le 2026-06-08. Vérifiez le démarrage du service.";

        var message = SmtpEmailTransport.BuildMessage(ConfiguredOptions(), DefaultBranding(), "ops@liakont.test", subject, body);

        message.Subject.Should().Be(subject);
        message.TextBody.Should().StartWith(body);
    }

    [Fact]
    public void BuildMessage_Throws_On_Blank_Recipient()
    {
        var act = () => SmtpEmailTransport.BuildMessage(ConfiguredOptions(), DefaultBranding(), "  ", "Sujet", "Corps");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BuildMessage_Branding_Overrides_The_Sender()
    {
        // Marque grise : un éditeur impose son propre expéditeur (nom + adresse). Le branding l'emporte sur le SMTP.
        var branding = new BrandingOptions
        {
            EmailFromName = "Conformité Acme",
            EmailFromAddress = "conformite@acme.example",
        };

        var message = SmtpEmailTransport.BuildMessage(ConfiguredOptions(), branding, "ops@acme.example", "Sujet", "Corps");

        var from = message.From.Mailboxes.Single();
        from.Name.Should().Be("Conformité Acme");
        from.Address.Should().Be("conformite@acme.example");
    }

    [Fact]
    public void AppendBrandingFooter_Adds_CommercialName_And_PoweredBy()
    {
        var branding = new BrandingOptions { CommercialName = "Acme Facture", PoweredByLiakont = true };

        string footer = SmtpEmailTransport.AppendBrandingFooter("Corps.", branding);

        footer.Should().StartWith("Corps.");
        footer.Should().Contain("Acme Facture");
        footer.Should().Contain("Propulsé par Liakont.");
    }

    [Fact]
    public void AppendBrandingFooter_Hides_PoweredBy_When_Disabled()
    {
        // L'éditeur désactive la mention : aucune trace « Liakont » ne doit subsister (blueprint.md §3.3).
        var branding = new BrandingOptions { CommercialName = "Acme Facture", PoweredByLiakont = false };

        string footer = SmtpEmailTransport.AppendBrandingFooter("Corps.", branding);

        footer.Should().Contain("Acme Facture");
        footer.Should().NotContain("Liakont");
    }

    [Fact]
    public void EffectiveFromAddress_Falls_Back_To_Smtp_When_Branding_Empty()
    {
        SmtpEmailTransport.EffectiveFromAddress(ConfiguredOptions(), DefaultBranding())
            .Should().Be("supervision@liakont.test");

        SmtpEmailTransport.EffectiveFromAddress(ConfiguredOptions(), new BrandingOptions { EmailFromAddress = "x@acme.example" })
            .Should().Be("x@acme.example");
    }

    [Fact]
    public async Task SendAsync_Is_NoOp_When_Not_Configured()
    {
        // Aucune config d'instance (store vide) ET appsettings désactivé → repli no-op (ADR-0039 §6).
        var transport = NewTransport(new SmtpOptions { Enabled = false }, new FakeInstanceEmailConfigStore());

        var act = async () => await transport.SendAsync("ops@liakont.test", "Sujet", "Corps");

        // Instance sans SMTP : pas d'exception (sinon retry infini du job) ; l'alerte reste au dashboard.
        await act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData("", "supervision@liakont.test")]
    [InlineData("smtp.example.test", "")]
    public async Task SendAsync_Is_NoOp_When_Enabled_But_Partially_Configured(string host, string fromAddress)
    {
        // Enabled=true mais un champ obligatoire manquant : on reste no-op (pas de throw), c'est la branche
        // qui distingue le vrai transport du stub quand l'opérateur active SMTP en oubliant un champ.
        var transport = NewTransport(
            new SmtpOptions { Enabled = true, Host = host, FromAddress = fromAddress },
            new FakeInstanceEmailConfigStore());

        var act = async () => await transport.SendAsync("ops@liakont.test", "Sujet", "Corps");

        await act.Should().NotThrowAsync();
    }

    // Construit le transport provider-aware avec des doubles (aucune config d'instance en base par défaut →
    // le transport retombe sur les appsettings, comme avant ADR-0039).
    private static SmtpEmailTransport NewTransport(SmtpOptions options, FakeInstanceEmailConfigStore store) =>
        new(
            Options.Create(options),
            Options.Create(DefaultBranding()),
            store,
            new FakeSecretProtector(),
            new FakeEmailOAuthTokenProvider(),
            NullLogger<SmtpEmailTransport>.Instance);
}
