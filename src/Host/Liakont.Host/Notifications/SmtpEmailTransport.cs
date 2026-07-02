namespace Liakont.Host.Notifications;

using System.Text;
using Liakont.Host.Configuration;
using Liakont.Host.InstanceEmail;
using Liakont.Modules.FleetSupervision.Application;
using Liakont.Modules.TenantSettings.Application;
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
/// <para>
/// PROVIDER-AWARE (ADR-0039) : la configuration d'INSTANCE en base (chiffrée) est <strong>autoritaire</strong>
/// quand elle est <c>Enabled</c> (SMTP basic / Gmail / O365 XOAUTH2) ; sinon repli sur <c>appsettings</c>
/// (<see cref="SmtpOptions"/>, SMTP basic) ; sinon no-op non bloquant. Le déchiffrement des secrets
/// (<see cref="ISecretProtector"/>) et l'obtention du jeton OAuth (<see cref="IEmailOAuthTokenProvider"/>) vivent
/// ici, au Host (monopole crypto, CLAUDE.md n°6/14). Le seam vendored reste (recipient, subject, body).
/// </para>
/// </summary>
internal sealed partial class SmtpEmailTransport : IEmailTransport, IEmailSendAvailability
{
    private readonly SmtpOptions _options;
    private readonly BrandingOptions _branding;
    private readonly IInstanceEmailConfigStore _configStore;
    private readonly ISecretProtector _secretProtector;
    private readonly IEmailOAuthTokenProvider _tokenProvider;
    private readonly ILogger<SmtpEmailTransport> _logger;

    public SmtpEmailTransport(
        IOptions<SmtpOptions> options,
        IOptions<BrandingOptions> branding,
        IInstanceEmailConfigStore configStore,
        ISecretProtector secretProtector,
        IEmailOAuthTokenProvider tokenProvider,
        ILogger<SmtpEmailTransport> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(branding);
        ArgumentNullException.ThrowIfNull(configStore);
        ArgumentNullException.ThrowIfNull(secretProtector);
        ArgumentNullException.ThrowIfNull(tokenProvider);
        _options = options.Value;
        _branding = branding.Value;
        _configStore = configStore;
        _secretProtector = secretProtector;
        _tokenProvider = tokenProvider;
        _logger = logger;
    }

    public async Task SendAsync(string recipient, string subject, string body, CancellationToken ct = default)
    {
        var resolved = await ResolveAsync(ct).ConfigureAwait(false);
        if (resolved is null)
        {
            // Instance sans SMTP configuré (ni base ni appsettings) : on NE lève PAS (sinon retry infini du
            // job) ; l'alerte reste visible au dashboard de supervision. Comportement équivalent au stub.
            LogSkipped(_logger, recipient, subject);
            return;
        }

        var message = BuildMessage(resolved.Shape, _branding, recipient, subject, body);

        using var client = new SmtpClient();
        var socketOptions = resolved.Shape.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect;
        await client.ConnectAsync(resolved.Shape.Host, resolved.Shape.Port, socketOptions, ct).ConfigureAwait(false);

        await AuthenticateAsync(client, resolved, ct).ConfigureAwait(false);

        await client.SendAsync(message, ct).ConfigureAwait(false);
        await client.DisconnectAsync(true, ct).ConfigureAwait(false);

        LogSent(_logger, recipient, subject);
    }

    /// <summary>
    /// Disponibilité EFFECTIVE d'envoi (BUG-31) : MÊME précédence que l'envoi
    /// (<see cref="IsAuthoritativeDbConfig"/> puis repli appsettings) mais SANS déchiffrer les
    /// secrets — une clé de chiffrement invalide fait échouer l'ENVOI (rattrapé par l'appelant),
    /// jamais la sonde. Les gardes du provisioning d'utilisateur interrogent CECI, jamais
    /// <see cref="SmtpOptions"/> seul.
    /// </summary>
    public async Task<bool> IsConfiguredAsync(CancellationToken ct = default)
    {
        var db = await _configStore.GetAsync(ct).ConfigureAwait(false);
        return IsAuthoritativeDbConfig(db) || _options.IsConfigured;
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

    /// <summary>
    /// Résout la configuration effective d'envoi (ADR-0039 §6) : la ligne DB <c>Enabled</c> et complète est
    /// AUTORITAIRE (SMTP basic / OAuth2, secrets déchiffrés en mémoire) ; à défaut, repli sur <c>appsettings</c>
    /// (SMTP basic) ; à défaut, <c>null</c> = no-op. Interne pour être testable sans réseau (précédence + kind +
    /// déchiffrement), l'envoi réel relevant d'une vérification de déploiement.
    /// </summary>
    internal async Task<ResolvedEmailConfig?> ResolveAsync(CancellationToken ct)
    {
        var db = await _configStore.GetAsync(ct).ConfigureAwait(false);
        if (IsAuthoritativeDbConfig(db))
        {
            return BuildFromDb(db!);
        }

        // Repli BOOTSTRAP uniquement (aucune ligne DB autoritaire) : appsettings, SMTP basic (ADR-0018).
        if (_options.IsConfigured)
        {
            return new ResolvedEmailConfig(_options, EmailProviderKind.SmtpBasic, _options.Password, OAuth: null);
        }

        return null;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Email envoyé à {Recipient} (sujet : {Subject}).")]
    private static partial void LogSent(ILogger logger, string recipient, string subject);

    [LoggerMessage(Level = LogLevel.Warning, Message = "SMTP non configuré : email à {Recipient} (sujet : {Subject}) non envoyé (l'alerte reste visible au dashboard).")]
    private static partial void LogSkipped(ILogger logger, string recipient, string subject);

    private async Task AuthenticateAsync(SmtpClient client, ResolvedEmailConfig resolved, CancellationToken ct)
    {
        switch (resolved.Kind)
        {
            case EmailProviderKind.GoogleOAuth2:
            case EmailProviderKind.MicrosoftOAuth2:
                // XOAUTH2 natif MailKit : jeton d'accès obtenu par rafraîchissement OAuth (aucun SDK).
                var accessToken = await _tokenProvider.GetAccessTokenAsync(resolved.OAuth!, ct).ConfigureAwait(false);
                await client.AuthenticateAsync(
                    new SaslMechanismOAuth2(resolved.Shape.Username, accessToken), ct).ConfigureAwait(false);
                break;

            default: // SmtpBasic : auth basique si un identifiant est présent (sinon relais ouvert autorisé).
                if (!string.IsNullOrEmpty(resolved.Shape.Username))
                {
                    await client.AuthenticateAsync(
                        resolved.Shape.Username, resolved.Password ?? string.Empty, ct).ConfigureAwait(false);
                }

                break;
        }
    }

    /// <summary>
    /// La ligne DB est AUTORITAIRE : présente, <c>Enabled</c> et complète (ADR-0039 §6). Précédence
    /// PARTAGÉE entre l'envoi (<see cref="ResolveAsync"/>) et la sonde de disponibilité
    /// (<see cref="IsConfiguredAsync"/>) — jamais deux dérivations divergentes. Aucun déchiffrement
    /// ici (null-checks seulement).
    /// </summary>
    private bool IsAuthoritativeDbConfig(InstanceEmailConfig? db) =>
        db is not null && db.Enabled && IsDbConfigured(db);

    /// <summary>
    /// Vrai si la config DB est utilisable : hôte + adresse d'expéditeur (config OU branding) présents ; pour un
    /// kind OAuth, les identifiants OAuth (client_id/secret/refresh_token) + utilisateur doivent être là — sinon
    /// on retombe (comme un SMTP basic partiellement configuré) sur le repli / no-op, jamais une auth incomplète.
    /// </summary>
    private bool IsDbConfigured(InstanceEmailConfig db)
    {
        if (string.IsNullOrWhiteSpace(db.Host))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(db.FromAddress) && string.IsNullOrWhiteSpace(_branding.EmailFromAddress))
        {
            return false;
        }

        if (db.Kind is EmailProviderKind.GoogleOAuth2 or EmailProviderKind.MicrosoftOAuth2)
        {
            return !string.IsNullOrWhiteSpace(db.Username)
                && !string.IsNullOrWhiteSpace(db.OAuthClientId)
                && !string.IsNullOrWhiteSpace(db.EncryptedOAuthClientSecret)
                && !string.IsNullOrWhiteSpace(db.EncryptedOAuthRefreshToken);
        }

        return true;
    }

    private ResolvedEmailConfig BuildFromDb(InstanceEmailConfig db)
    {
        var shape = new SmtpOptions
        {
            Enabled = true,
            Host = db.Host,
            Port = db.Port,
            UseStartTls = db.UseStartTls,
            Username = db.Username,
            FromAddress = db.FromAddress,
            FromName = db.FromName,
        };

        switch (db.Kind)
        {
            case EmailProviderKind.GoogleOAuth2:
            case EmailProviderKind.MicrosoftOAuth2:
                var request = new EmailOAuthTokenRequest
                {
                    Kind = db.Kind,
                    ClientId = db.OAuthClientId ?? string.Empty,
                    ClientSecret = Unprotect(db.EncryptedOAuthClientSecret, EmailSecretPurposes.OAuthClientSecret),
                    RefreshToken = Unprotect(db.EncryptedOAuthRefreshToken, EmailSecretPurposes.OAuthRefreshToken),
                    TenantId = db.OAuthTenantId,
                };
                return new ResolvedEmailConfig(shape, db.Kind, Password: null, request);

            default: // SmtpBasic
                var password = Unprotect(db.EncryptedSmtpPassword, EmailSecretPurposes.SmtpPassword);
                return new ResolvedEmailConfig(shape, EmailProviderKind.SmtpBasic, password, OAuth: null);
        }
    }

    // Déchiffrement EN MÉMOIRE uniquement (jamais reversé au descripteur ni journalisé — CLAUDE.md n°10) ; un
    // ciphertext absent donne une chaîne vide (secret non saisi).
    private string Unprotect(string? ciphertext, string purpose) =>
        string.IsNullOrEmpty(ciphertext) ? string.Empty : _secretProtector.Unprotect(ciphertext, purpose);
}
