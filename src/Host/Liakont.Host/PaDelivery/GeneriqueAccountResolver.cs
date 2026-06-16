namespace Liakont.Host.PaDelivery;

using System.Globalization;
using Liakont.Modules.TenantSettings.Application;
using Liakont.Modules.Transmission.Contracts;
using Liakont.PaClients.Generique;

/// <summary>
/// Implémentation Host de <see cref="IGeneriqueAccountResolver"/> : traduit le
/// <see cref="PaAccountDescriptor"/> NON SENSIBLE d'un tenant en <see cref="GeneriqueAccountConfig"/>
/// (canal + cible), et DÉCHIFFRE l'éventuel mot de passe SMTP par tenant via le coffre
/// (<see cref="ISecretProtector"/>) — c'est la frontière où le secret CHIFFRÉ par tenant redevient clair,
/// EN MÉMOIRE uniquement (CLAUDE.md n°10). Le descripteur ne porte que des valeurs non sensibles : le mot
/// de passe y figure UNIQUEMENT sous forme chiffrée (jamais en clair, jamais versionné — CLAUDE.md n°10/18).
/// On bloque plutôt que de livrer faux (CLAUDE.md n°3) : canal/cible absents → exception.
/// </summary>
internal sealed class GeneriqueAccountResolver : IGeneriqueAccountResolver
{
    /// <summary>Clé de paramètre : canal de livraison (« Email » / « FileDeposit »).</summary>
    public const string MethodKey = "DeliveryMethod";

    /// <summary>Clé de paramètre : cible non sensible (adresse email / dossier).</summary>
    public const string TargetKey = "Target";

    /// <summary>Clé de paramètre : hôte SMTP par tenant (optionnel — sinon SMTP d'instance, ADR-0018).</summary>
    public const string SmtpHostKey = "SmtpHost";

    /// <summary>Clé de paramètre : port SMTP par tenant (optionnel).</summary>
    public const string SmtpPortKey = "SmtpPort";

    /// <summary>Clé de paramètre : STARTTLS par tenant (optionnel, défaut true).</summary>
    public const string SmtpUseStartTlsKey = "SmtpUseStartTls";

    /// <summary>Clé de paramètre : identifiant SMTP par tenant (optionnel).</summary>
    public const string SmtpUsernameKey = "SmtpUsername";

    /// <summary>Clé de paramètre : mot de passe SMTP par tenant CHIFFRÉ (texte protégé, jamais en clair).</summary>
    public const string SmtpPasswordProtectedKey = "SmtpPasswordProtected";

    private readonly ISecretProtector _secretProtector;

    public GeneriqueAccountResolver(ISecretProtector secretProtector)
    {
        ArgumentNullException.ThrowIfNull(secretProtector);
        _secretProtector = secretProtector;
    }

    /// <inheritdoc />
    public GeneriqueAccountConfig Resolve(PaAccountDescriptor account)
    {
        ArgumentNullException.ThrowIfNull(account);

        var settings = account.Settings;

        if (!settings.TryGetValue(MethodKey, out var methodValue)
            || !Enum.TryParse<DocumentDeliveryMethod>(methodValue, ignoreCase: true, out var method))
        {
            throw new InvalidOperationException(
                $"Compte générique du tenant « {account.TenantId} » : canal de livraison « {MethodKey} » "
                + "absent ou invalide (attendu : « Email » ou « FileDeposit ») — résolution bloquée.");
        }

        if (!settings.TryGetValue(TargetKey, out var target) || string.IsNullOrWhiteSpace(target))
        {
            throw new InvalidOperationException(
                $"Compte générique du tenant « {account.TenantId} » : cible « {TargetKey} » absente "
                + "(adresse email ou dossier) — résolution bloquée.");
        }

        return new GeneriqueAccountConfig
        {
            Method = method,
            Target = target,
            SmtpAuth = ResolveSmtpAuth(settings),
        };
    }

    private SmtpDeliveryAuthentication? ResolveSmtpAuth(IReadOnlyDictionary<string, string> settings)
    {
        // SMTP par tenant OPTIONNEL : sans hôte, on réutilise le SMTP d'instance (ADR-0018).
        if (!settings.TryGetValue(SmtpHostKey, out var host) || string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        int port = settings.TryGetValue(SmtpPortKey, out var portValue)
                   && int.TryParse(portValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPort)
            ? parsedPort
            : 587;

        bool useStartTls = !settings.TryGetValue(SmtpUseStartTlsKey, out var tlsValue)
                           || !bool.TryParse(tlsValue, out var parsedTls)
                           || parsedTls;

        settings.TryGetValue(SmtpUsernameKey, out var username);

        // Le mot de passe est stocké CHIFFRÉ (texte protégé) : déchiffré ici, en mémoire uniquement
        // (jamais en clair dans le descripteur ni versionné — CLAUDE.md n°10).
        string password = string.Empty;
        if (settings.TryGetValue(SmtpPasswordProtectedKey, out var protectedPassword)
            && !string.IsNullOrWhiteSpace(protectedPassword))
        {
            password = _secretProtector.Unprotect(protectedPassword);
        }

        return new SmtpDeliveryAuthentication
        {
            Host = host,
            Port = port,
            UseStartTls = useStartTls,
            Username = username ?? string.Empty,
            Password = password,
        };
    }
}
