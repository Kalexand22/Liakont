namespace Liakont.Host.Tests.Unit.Notifications;

using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;
using Xunit;

/// <summary>
/// Couvre la composition du message (sans connexion réseau) et le mode non configuré du transport SMTP
/// (ADR-0018, SUP03). L'envoi SMTP réel relève d'une vérification de déploiement (serveur requis).
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

    [Fact]
    public void BuildMessage_Sets_From_To_Subject_And_Body()
    {
        var message = SmtpEmailTransport.BuildMessage(ConfiguredOptions(), "ops@liakont.test", "Sujet", "Corps du message.");

        var from = message.From.Mailboxes.Single();
        from.Address.Should().Be("supervision@liakont.test");
        from.Name.Should().Be("Liakont Supervision");
        message.To.Mailboxes.Single().Address.Should().Be("ops@liakont.test");
        message.Subject.Should().Be("Sujet");
        message.TextBody.Should().Be("Corps du message.");
    }

    [Fact]
    public void BuildMessage_Preserves_French_Accents_In_Utf8()
    {
        const string subject = "[Liakont] Alerte critique — tenant acme";
        const string body = "L'agent ne répond plus depuis le 2026-06-08. Vérifiez le démarrage du service.";

        var message = SmtpEmailTransport.BuildMessage(ConfiguredOptions(), "ops@liakont.test", subject, body);

        message.Subject.Should().Be(subject);
        message.TextBody.Should().Be(body);
    }

    [Fact]
    public void BuildMessage_Throws_On_Blank_Recipient()
    {
        var act = () => SmtpEmailTransport.BuildMessage(ConfiguredOptions(), "  ", "Sujet", "Corps");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task SendAsync_Is_NoOp_When_Not_Configured()
    {
        var transport = new SmtpEmailTransport(
            Options.Create(new SmtpOptions { Enabled = false }),
            NullLogger<SmtpEmailTransport>.Instance);

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
        var transport = new SmtpEmailTransport(
            Options.Create(new SmtpOptions { Enabled = true, Host = host, FromAddress = fromAddress }),
            NullLogger<SmtpEmailTransport>.Instance);

        var act = async () => await transport.SendAsync("ops@liakont.test", "Sujet", "Corps");

        await act.Should().NotThrowAsync();
    }
}
