namespace Liakont.Host.PaDelivery;

using System.IO;
using Liakont.Host.Configuration;
using Liakont.Host.Notifications;
using Liakont.Modules.Transmission.Contracts;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

/// <summary>
/// Canal de livraison par EMAIL (F16 §6.2) implémentant <see cref="IDocumentDeliveryChannel"/> au Host —
/// le seul endroit où MailKit/MimeKit sont référencés (le plug-in PA générique ne voit que l'abstraction).
/// Compose un message MIME AVEC PIÈCE JOINTE (le Factur-X scellé) — ce que le socle
/// <see cref="Stratum.Modules.Notification.Contracts.IEmailTransport"/> NE PEUT PAS faire (corps texte
/// seul) : on NE réutilise donc PAS <c>IEmailTransport</c> et on NE modifie PAS le socle vendored
/// (CLAUDE.md n°11/20). On réutilise en revanche la config/connexion SMTP de NIVEAU INSTANCE (ADR-0018,
/// <see cref="SmtpOptions"/>) ; un compte peut fournir ses propres identifiants SMTP (déchiffrés par
/// tenant, <see cref="DocumentDeliveryRequest.SmtpAuth"/>) pour envoyer depuis sa propre connexion. Le mot
/// de passe n'est JAMAIS journalisé (CLAUDE.md n°18).
/// </summary>
internal sealed partial class EmailDocumentDeliveryChannel : IDocumentDeliveryChannel
{
    private readonly SmtpOptions _options;
    private readonly BrandingOptions _branding;
    private readonly ILogger<EmailDocumentDeliveryChannel> _logger;

    public EmailDocumentDeliveryChannel(
        IOptions<SmtpOptions> options,
        IOptions<BrandingOptions> branding,
        ILogger<EmailDocumentDeliveryChannel> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(branding);
        _options = options.Value;
        _branding = branding.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public DocumentDeliveryMethod Method => DocumentDeliveryMethod.Email;

    /// <inheritdoc />
    public async Task DeliverAsync(DocumentDeliveryRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Target);

        if (request.Content.IsEmpty)
        {
            // Bloquer plutôt qu'envoyer un email sans le Factur-X (CLAUDE.md n°3).
            throw new InvalidOperationException(
                "Factur-X vide : transmission par email bloquée (jamais d'envoi à vide).");
        }

        var smtp = ResolveSmtp(_options, _branding, request.SmtpAuth);
        if (!smtp.IsConfigured)
        {
            // À la différence des emails d'alerte (no-op tolérable), la transmission d'un Factur-X DOIT
            // aboutir ou bloquer : on ne prétend jamais un envoi réussi (CLAUDE.md n°3). Le pipeline
            // re-tentera (erreur technique).
            throw new InvalidOperationException(
                "SMTP non configuré : impossible de transmettre le Factur-X par email (CLAUDE.md n°3).");
        }

        var message = BuildMessage(
            smtp.FromName,
            smtp.FromAddress,
            request.Target,
            request.Subject,
            request.Body,
            request.FileName,
            request.ContentType,
            request.Content);

        using var client = new SmtpClient();
        var socketOptions = smtp.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect;
        await client.ConnectAsync(smtp.Host, smtp.Port, socketOptions, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(smtp.Username))
        {
            await client.AuthenticateAsync(smtp.Username, smtp.Password, cancellationToken).ConfigureAwait(false);
        }

        await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
        await client.DisconnectAsync(true, cancellationToken).ConfigureAwait(false);

        LogDelivered(_logger, request.Target, request.FileName);
    }

    /// <summary>
    /// Compose le message MIME multipart : corps texte (français — CLAUDE.md n°12) + PIÈCE JOINTE binaire
    /// (le Factur-X). Statique et interne pour être testable sans connexion réseau (From/To/Subject/Body +
    /// présence et nom de la pièce jointe).
    /// </summary>
    internal static MimeMessage BuildMessage(
        string fromName,
        string fromAddress,
        string recipient,
        string? subject,
        string? body,
        string fileName,
        string contentType,
        ReadOnlyMemory<byte> content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(fromName, fromAddress));
        message.To.Add(MailboxAddress.Parse(recipient));
        message.Subject = string.IsNullOrWhiteSpace(subject) ? "Facture électronique" : subject;

        var textPart = new TextPart("plain")
        {
            Text = string.IsNullOrWhiteSpace(body)
                ? "Veuillez trouver la facture au format Factur-X en pièce jointe."
                : body,
        };

        var attachment = new MimePart(ContentType.Parse(contentType))
        {
            Content = new MimeContent(new MemoryStream(content.ToArray())),
            ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
            ContentTransferEncoding = ContentEncoding.Base64,
            FileName = fileName,
        };

        message.Body = new Multipart("mixed") { textPart, attachment };
        return message;
    }

    /// <summary>
    /// Identifiants SMTP EFFECTIFS : priorité aux identifiants par tenant (<paramref name="perTenant"/>),
    /// repli sur le SMTP de niveau instance (ADR-0018). L'expéditeur (From) reste celui de l'instance
    /// (branding) en V1. Méthode pure pour être testable sans réseau. Le mot de passe n'apparaît dans aucun log.
    /// </summary>
    internal static EffectiveSmtp ResolveSmtp(
        SmtpOptions options,
        BrandingOptions branding,
        SmtpDeliveryAuthentication? perTenant)
    {
        string fromAddress = !string.IsNullOrWhiteSpace(branding.EmailFromAddress)
            ? branding.EmailFromAddress
            : options.FromAddress;
        string fromName = !string.IsNullOrWhiteSpace(branding.EmailFromName)
            ? branding.EmailFromName
            : options.FromName;

        if (perTenant is not null)
        {
            return new EffectiveSmtp
            {
                Host = perTenant.Host,
                Port = perTenant.Port,
                UseStartTls = perTenant.UseStartTls,
                Username = perTenant.Username,
                Password = perTenant.Password,
                FromAddress = fromAddress,
                FromName = fromName,
                HasConnection = !string.IsNullOrWhiteSpace(perTenant.Host),
            };
        }

        return new EffectiveSmtp
        {
            Host = options.Host,
            Port = options.Port,
            UseStartTls = options.UseStartTls,
            Username = options.Username,
            Password = options.Password,
            FromAddress = fromAddress,
            FromName = fromName,
            HasConnection = options.Enabled && !string.IsNullOrWhiteSpace(options.Host),
        };
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Factur-X transmis par email à {Recipient} (pièce jointe : {FileName}).")]
    private static partial void LogDelivered(ILogger logger, string recipient, string fileName);

    /// <summary>
    /// Paramètres SMTP effectifs résolus (instance ou par tenant). Type <c>class</c> (et non <c>record</c>) :
    /// il porte un mot de passe en mémoire ; un <c>record</c> l'imprimerait via <c>ToString()</c> (P1 — fuite
    /// de secret, CLAUDE.md n°18). Aucun membre n'est rendu en texte ; aucun log ne reçoit le mot de passe.
    /// </summary>
    internal sealed class EffectiveSmtp
    {
        public required string Host { get; init; }

        public required int Port { get; init; }

        public required bool UseStartTls { get; init; }

        public required string Username { get; init; }

        public required string Password { get; init; }

        public required string FromAddress { get; init; }

        public required string FromName { get; init; }

        public required bool HasConnection { get; init; }

        /// <summary>Le canal est utilisable : connexion ET expéditeur présents.</summary>
        public bool IsConfigured => HasConnection && !string.IsNullOrWhiteSpace(FromAddress);
    }
}
