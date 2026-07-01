namespace Liakont.Host.InstanceEmail;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.FleetSupervision.Application;
using Liakont.Modules.TenantSettings.Application;
using Microsoft.Extensions.Logging;
using Stratum.Modules.Notification.Contracts;

/// <summary>
/// Implémentation Host de <see cref="IInstanceEmailConfigService"/> (ADR-0039). Le magasin (module
/// FleetSupervision) ne voit que du ciphertext ; ICI (Host) vit le monopole du chiffrement/déchiffrement via
/// <see cref="ISecretProtector"/> (précédent <c>GeneriqueAccountResolver</c>, CLAUDE.md n°6/14). Au save, les
/// secrets non vides sont chiffrés sous leur purpose dédié ; un secret VIDE conserve le ciphertext existant
/// (lit-puis-conserve). Le DTO de lecture n'expose que des booléens <c>Has*</c>. Le clair ne quitte jamais ce
/// service ; aucun secret n'est journalisé (CLAUDE.md n°10/18).
/// </summary>
internal sealed partial class InstanceEmailConfigService : IInstanceEmailConfigService
{
    private const string TestSubject = "Email de test — Liakont";

    private const string TestBody =
        "Ceci est un email de test envoyé depuis la configuration d'envoi d'emails de votre instance Liakont. "
        + "Si vous le recevez, la configuration est fonctionnelle.";

    private readonly IInstanceEmailConfigStore _store;
    private readonly ISecretProtector _secretProtector;
    private readonly IEmailTransport _emailTransport;
    private readonly ILogger<InstanceEmailConfigService> _logger;

    public InstanceEmailConfigService(
        IInstanceEmailConfigStore store,
        ISecretProtector secretProtector,
        IEmailTransport emailTransport,
        ILogger<InstanceEmailConfigService> logger)
    {
        _store = store;
        _secretProtector = secretProtector;
        _emailTransport = emailTransport;
        _logger = logger;
    }

    public async Task<InstanceEmailConfigViewModel> GetAsync(CancellationToken cancellationToken = default)
    {
        var config = await _store.GetAsync(cancellationToken).ConfigureAwait(false);
        if (config is null)
        {
            // Aucune config enregistrée : formulaire par défaut (aucun secret, désactivé).
            return new InstanceEmailConfigViewModel { Form = new InstanceEmailConfigForm() };
        }

        return new InstanceEmailConfigViewModel
        {
            Form = new InstanceEmailConfigForm
            {
                Kind = config.Kind.ToString(),
                Host = config.Host,
                Port = config.Port,
                UseStartTls = config.UseStartTls,
                FromAddress = config.FromAddress,
                FromName = config.FromName,
                Username = config.Username,
                OAuthClientId = config.OAuthClientId ?? string.Empty,
                OAuthTenantId = config.OAuthTenantId ?? string.Empty,
                Enabled = config.Enabled,

                // Secrets JAMAIS pré-remplis (le DTO ne les expose pas) : champ vide = inchangé au prochain save.
                SmtpPassword = string.Empty,
                OAuthClientSecret = string.Empty,
                OAuthRefreshToken = string.Empty,
            },
            HasSmtpPassword = !string.IsNullOrEmpty(config.EncryptedSmtpPassword),
            HasOAuthClientSecret = !string.IsNullOrEmpty(config.EncryptedOAuthClientSecret),
            HasOAuthRefreshToken = !string.IsNullOrEmpty(config.EncryptedOAuthRefreshToken),
        };
    }

    public async Task SaveAsync(InstanceEmailConfigInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!Enum.TryParse<EmailProviderKind>(input.Kind, ignoreCase: false, out var kind))
        {
            // Défense en profondeur : le sélecteur ne propose que des kinds valides ; jamais une valeur devinée.
            throw new ArgumentException($"Type de fournisseur email non reconnu : « {input.Kind} ».", nameof(input));
        }

        // Lit-puis-conserve : on relit l'existant pour PRÉSERVER un secret non re-saisi (champ vide au save).
        var existing = await _store.GetAsync(cancellationToken).ConfigureAwait(false);

        var config = new InstanceEmailConfig
        {
            Kind = kind,
            Host = input.Host?.Trim() ?? string.Empty,
            Port = input.Port,
            UseStartTls = input.UseStartTls,
            FromAddress = input.FromAddress?.Trim() ?? string.Empty,
            FromName = input.FromName?.Trim() ?? string.Empty,
            Username = input.Username?.Trim() ?? string.Empty,
            EncryptedSmtpPassword = ProtectOrKeep(
                input.SmtpPassword, EmailSecretPurposes.SmtpPassword, existing?.EncryptedSmtpPassword),
            OAuthClientId = NullIfBlank(input.OAuthClientId),
            OAuthTenantId = NullIfBlank(input.OAuthTenantId),
            EncryptedOAuthClientSecret = ProtectOrKeep(
                input.OAuthClientSecret, EmailSecretPurposes.OAuthClientSecret, existing?.EncryptedOAuthClientSecret),
            EncryptedOAuthRefreshToken = ProtectOrKeep(
                input.OAuthRefreshToken, EmailSecretPurposes.OAuthRefreshToken, existing?.EncryptedOAuthRefreshToken),
            Enabled = input.Enabled,
        };

        await _store.UpsertAsync(config, cancellationToken).ConfigureAwait(false);
    }

    public async Task<EmailTestResult> SendTestAsync(string recipient, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(recipient))
        {
            return EmailTestResult.Failed("Renseignez une adresse destinataire pour l'email de test.");
        }

        var config = await _store.GetAsync(cancellationToken).ConfigureAwait(false);
        if (config is null || !config.Enabled)
        {
            return EmailTestResult.Failed(
                "La configuration d'envoi d'emails n'est pas activée. Enregistrez et activez une configuration avant d'envoyer un test.");
        }

        if (string.IsNullOrWhiteSpace(config.Host))
        {
            return EmailTestResult.Failed("Hôte SMTP manquant : renseignez l'hôte avant d'envoyer un email de test.");
        }

        if (string.IsNullOrWhiteSpace(config.FromAddress))
        {
            return EmailTestResult.Failed("Adresse d'expéditeur manquante : renseignez-la avant d'envoyer un email de test.");
        }

        // Un kind OAuth activé mais INCOMPLET (ex. refresh_token non saisi) est traité par le transport comme
        // « non configuré » → il no-op silencieusement (ou retombe sur appsettings). On refuse le test ICI, avec
        // les MÊMES exigences que le transport (SmtpEmailTransport.IsDbConfigured), pour ne JAMAIS annoncer un
        // « envoyé » alors que rien n'est parti (faux-vert de la surface de vérification, CLAUDE.md review n°8).
        if (config.Kind is EmailProviderKind.GoogleOAuth2 or EmailProviderKind.MicrosoftOAuth2
            && (string.IsNullOrWhiteSpace(config.Username)
                || string.IsNullOrWhiteSpace(config.OAuthClientId)
                || string.IsNullOrWhiteSpace(config.EncryptedOAuthClientSecret)
                || string.IsNullOrWhiteSpace(config.EncryptedOAuthRefreshToken)))
        {
            return EmailTestResult.Failed(
                "Configuration OAuth incomplète : renseignez l'identifiant, le client_id, le client_secret et le refresh_token avant d'envoyer un email de test.");
        }

        try
        {
            await _emailTransport.SendAsync(recipient, TestSubject, TestBody, cancellationToken).ConfigureAwait(false);
            return EmailTestResult.Succeeded($"Email de test envoyé à {recipient}. Vérifiez la boîte de réception.");
        }
        catch (Exception ex)
        {
            // L'échec reste VISIBLE (résultat), tracé, et SANS secret (CLAUDE.md n°10) — jamais avalé ni levé vers l'UI.
            LogTestFailed(_logger, ex, recipient);
            return EmailTestResult.Failed(
                $"L'envoi de l'email de test à {recipient} a échoué : vérifiez la configuration SMTP/OAuth. Le détail technique est dans les journaux.");
        }
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [LoggerMessage(Level = LogLevel.Error, Message = "Échec de l'envoi de l'email de test à {Recipient}.")]
    private static partial void LogTestFailed(ILogger logger, Exception exception, string recipient);

    /// <summary>Chiffre un secret non vide sous son purpose ; vide/blanc = conserve le ciphertext existant.</summary>
    private string? ProtectOrKeep(string? clear, string purpose, string? existingCiphertext) =>
        string.IsNullOrWhiteSpace(clear) ? existingCiphertext : _secretProtector.Protect(clear, purpose);
}
