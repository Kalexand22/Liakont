namespace Liakont.Host.Tests.Unit.PaDelivery;

using System.IO;
using System.Linq;
using System.Text;
using FluentAssertions;
using Liakont.Host.Configuration;
using Liakont.Host.Notifications;
using Liakont.Host.PaDelivery;
using Liakont.Modules.Transmission.Contracts;
using MimeKit;
using Xunit;

/// <summary>
/// Composition du message MIME AVEC pièce jointe (le Factur-X) et résolution des identifiants SMTP
/// effectifs (instance ADR-0018 vs par tenant), sans connexion réseau (F16 §6.2). L'envoi SMTP réel
/// relève d'une vérification de déploiement (comme <c>SmtpEmailTransport</c>).
/// </summary>
public sealed class EmailDocumentDeliveryChannelTests
{
    private static readonly byte[] SampleFacturX = Encoding.ASCII.GetBytes("%PDF-1.7 factur-x sample");

    private static SmtpOptions InstanceSmtp() => new()
    {
        Enabled = true,
        Host = "smtp.instance.test",
        Port = 587,
        Username = "instance-user",
        Password = "instance-secret",
        FromAddress = "facture@liakont.test",
        FromName = "Liakont",
    };

    [Fact]
    public void BuildMessage_Carries_The_FacturX_As_An_Attachment()
    {
        var message = EmailDocumentDeliveryChannel.BuildMessage(
            "Liakont",
            "facture@liakont.test",
            "pa@tenant.test",
            "Facture F-2026-001",
            "Veuillez trouver ci-joint la facture.",
            "factur-x_F-2026-001.pdf",
            "application/pdf",
            SampleFacturX);

        message.From.Mailboxes.Single().Address.Should().Be("facture@liakont.test");
        message.To.Mailboxes.Single().Address.Should().Be("pa@tenant.test");
        message.Subject.Should().Be("Facture F-2026-001");

        var attachment = message.Attachments.OfType<MimePart>().Single();
        attachment.FileName.Should().Be("factur-x_F-2026-001.pdf");

        using var decoded = new MemoryStream();
        attachment.Content!.DecodeTo(decoded);
        decoded.ToArray().Should().Equal(SampleFacturX, "la pièce jointe transmet l'artefact exact (jamais régénéré)");
    }

    [Fact]
    public void BuildMessage_Throws_On_Blank_Recipient()
    {
        var act = () => EmailDocumentDeliveryChannel.BuildMessage(
            "Liakont", "facture@liakont.test", "  ", "Sujet", "Corps", "f.pdf", "application/pdf", SampleFacturX);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ResolveSmtp_Uses_Instance_Connection_By_Default()
    {
        var smtp = EmailDocumentDeliveryChannel.ResolveSmtp(InstanceSmtp(), new BrandingOptions(), perTenant: null);

        smtp.Host.Should().Be("smtp.instance.test");
        smtp.Username.Should().Be("instance-user");
        smtp.Password.Should().Be("instance-secret");
        smtp.FromAddress.Should().Be("facture@liakont.test");
        smtp.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public void ResolveSmtp_Prefers_Per_Tenant_Credentials_When_Provided()
    {
        var perTenant = new SmtpDeliveryAuthentication
        {
            Host = "smtp.tenant.test",
            Port = 2525,
            UseStartTls = false,
            Username = "tenant-user",
            Password = "tenant-secret",
        };

        var smtp = EmailDocumentDeliveryChannel.ResolveSmtp(InstanceSmtp(), new BrandingOptions(), perTenant);

        smtp.Host.Should().Be("smtp.tenant.test");
        smtp.Port.Should().Be(2525);
        smtp.UseStartTls.Should().BeFalse();
        smtp.Username.Should().Be("tenant-user");
        smtp.Password.Should().Be("tenant-secret");
        smtp.IsConfigured.Should().BeTrue("la connexion par tenant est présente et l'expéditeur d'instance reste défini");
    }

    [Fact]
    public void ResolveSmtp_Is_Not_Configured_When_Instance_Disabled()
    {
        var disabled = new SmtpOptions { Enabled = false, Host = "smtp.instance.test", FromAddress = "x@liakont.test" };

        EmailDocumentDeliveryChannel.ResolveSmtp(disabled, new BrandingOptions(), perTenant: null)
            .IsConfigured.Should().BeFalse();
    }
}
