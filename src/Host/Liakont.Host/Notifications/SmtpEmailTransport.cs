namespace Liakont.Host.Notifications;

using System.Text;
using Liakont.Host.Configuration;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Stratum.Modules.Notification.Contracts;

/// <summary>
/// Transport SMTP réel (ADR-0018, SUP03) implémentant l'abstraction <see cref="IEmailTransport"/> du module
/// Notification du socle (vendored, NON modifié). Enregistré au composition root EN REMPLACEMENT du
/// <c>StubEmailTransport</c> ; consommé par <c>EmailSendJobHandler</c> au moment de la livraison (le retry du
/// pipeline de jobs rend l'échec d'envoi non bloquant). Le mot de passe SMTP n'est jamais journalisé
/// (seuls destinataire + sujet le sont — CLAUDE.md n°18).
/// </summary>
internal sealed partial class SmtpEmailTransport : IEmailTransport
{
    private readonly SmtpOptions _options;
    private readonly BrandingOptions _branding;
    private readonly ILogger<SmtpEmailTransport> _logger;

    public SmtpEmailTransport(IOptions<SmtpOptions> options, IOptions<BrandingOptions> branding, ILogger<SmtpEmailTransport> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(branding);
        _options = options.Value;
        _branding = branding.Value;
        _logger = logger;
    }

    public async Task SendAsync(string recipient, string subject, string body, CancellationToken ct = default)
    {
        if (!IsConfigured())
        {
            // Instance sans SMTP configuré : on NE lève PAS (sinon retry infini du job) ; l'alerte reste
            // visible au dashboard de supervision. Comportement équivalent au stub (journalise).
            LogSkipped(_logger, recipient, subject);
            return;
        }

        var message = BuildMessage(_options, _branding, recipient, subject, body);

        using var client = new SmtpClient();
        var socketOptions = _options.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect;
        await client.ConnectAsync(_options.Host, _options.Port, socketOptions, ct).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(_options.Username))
        {
            await client.AuthenticateAsync(_options.Username, _options.Password, ct).ConfigureAwait(false);
        }

        await client.SendAsync(message, ct).ConfigureAwait(false);
        await client.DisconnectAsync(true, ct).ConfigureAwait(false);

        LogSent(_logger, recipient, subject);
    }

    /// <summary>
    /// Compose le message MIME. Le sujet et le corps sont en UTF-8 (français accentué — CLAUDE.md n°12).
    /// L'EXPÉDITEUR (nom + adresse) et le PIED DE PAGE relèvent du branding d'instance (BRD01, marque
    /// grise) : <see cref="BrandingOptions.EmailFromName"/>/<see cref="BrandingOptions.EmailFromAddress"/>
    /// l'emportent sur la config SMTP quand ils sont renseignés (l'adresse SMTP reste le repli — le
    /// serveur d'envoi contraint les expéditeurs autorisés). Statique et interne pour être testable sans
    /// connexion réseau (From / To / Subject / Body).
    /// </summary>
    internal static MimeMessage BuildMessage(SmtpOptions options, BrandingOptions branding, string recipient, string subject, string body)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(branding);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);

        string fromName = !string.IsNullOrWhiteSpace(branding.EmailFromName) ? branding.EmailFromName : options.FromName;
        string fromAddress = EffectiveFromAddress(options, branding);

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(fromName, fromAddress));
        message.To.Add(MailboxAddress.Parse(recipient));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = AppendBrandingFooter(body, branding) };
        return message;
    }

    /// <summary>
    /// Adresse d'expéditeur effective : branding d'instance prioritaire, repli sur la config SMTP.
    /// Sert AUSSI à la garde <see cref="IsConfigured"/> (un transport actif sans adresse reste no-op).
    /// </summary>
    internal static string EffectiveFromAddress(SmtpOptions options, BrandingOptions branding) =>
        !string.IsNullOrWhiteSpace(branding.EmailFromAddress) ? branding.EmailFromAddress : options.FromAddress;

    /// <summary>
    /// Ajoute le pied de page brandé (marque grise BRD01) : nom commercial, mention de pied facultative et,
    /// si activée, la mention technique discrète « propulsé par Liakont ». Plein texte (le transport est en
    /// text/plain) — séparé du corps par le séparateur de signature standard « -- ».
    /// </summary>
    internal static string AppendBrandingFooter(string body, BrandingOptions branding)
    {
        var sb = new StringBuilder(body);
        sb.Append("\n\n-- \n");
        sb.Append(branding.EffectiveCommercialName);
        if (!string.IsNullOrWhiteSpace(branding.FooterText))
        {
            sb.Append('\n').Append(branding.FooterText);
        }

        if (branding.PoweredByLiakont)
        {
            sb.Append("\nPropulsé par Liakont.");
        }

        return sb.ToString();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Email envoyé à {Recipient} (sujet : {Subject}).")]
    private static partial void LogSent(ILogger logger, string recipient, string subject);

    [LoggerMessage(Level = LogLevel.Warning, Message = "SMTP non configuré : email à {Recipient} (sujet : {Subject}) non envoyé (l'alerte reste visible au dashboard).")]
    private static partial void LogSkipped(ILogger logger, string recipient, string subject);

    private bool IsConfigured() =>
        _options.Enabled
        && !string.IsNullOrWhiteSpace(_options.Host)
        && !string.IsNullOrWhiteSpace(EffectiveFromAddress(_options, _branding));
}
