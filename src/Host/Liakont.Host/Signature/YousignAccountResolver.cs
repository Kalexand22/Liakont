namespace Liakont.Host.Signature;

using Liakont.Modules.Signature.Application;
using Liakont.Modules.Signature.Contracts;
using Liakont.Modules.TenantSettings.Application;
using Liakont.SignatureProviders.Yousign;

/// <summary>
/// Implémentation Host de <see cref="IYousignAccountResolver"/> (ADR-0029 §6) : traduit le
/// <see cref="SignatureProviderAccount"/> NON SENSIBLE d'un tenant en <see cref="YousignAccountConfig"/>, et
/// DÉCHIFFRE la clé API + le secret webhook par tenant via le coffre (<see cref="ISecretProtector"/>) — c'est
/// la frontière où les secrets CHIFFRÉS redeviennent clairs, EN MÉMOIRE uniquement (CLAUDE.md n°10). Le plug-in
/// ne référence que <c>Signature.Contracts</c> (INV-YOUSIGN-2) : il ne peut pas atteindre le coffre, d'où cette
/// implémentation au Host. On bloque plutôt que d'appeler sans authentification (CLAUDE.md n°3) : un secret
/// absent → exception.
/// </summary>
internal sealed class YousignAccountResolver : IYousignAccountResolver
{
    private readonly ISecretProtector _secretProtector;

    public YousignAccountResolver(ISecretProtector secretProtector)
    {
        ArgumentNullException.ThrowIfNull(secretProtector);
        _secretProtector = secretProtector;
    }

    public YousignAccountConfig Resolve(SignatureProviderAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        var settings = account.Settings;

        var environment = settings.TryGetValue(SignatureAccountSettingKeys.Environment, out var envValue)
            && Enum.TryParse<YousignEnvironment>(envValue, ignoreCase: true, out var parsed)
                ? parsed
                : YousignEnvironment.Sandbox;

        if (!settings.TryGetValue(SignatureAccountSettingKeys.EncryptedApiKey, out var encryptedApiKey)
            || string.IsNullOrWhiteSpace(encryptedApiKey))
        {
            throw new InvalidOperationException(
                $"Compte Yousign du tenant « {account.CompanyId} » : clé API absente — résolution bloquée. "
                + "Saisissez la clé API du compte de signature.");
        }

        if (!settings.TryGetValue(SignatureAccountSettingKeys.EncryptedWebhookSecret, out var encryptedWebhookSecret)
            || string.IsNullOrWhiteSpace(encryptedWebhookSecret))
        {
            throw new InvalidOperationException(
                $"Compte Yousign du tenant « {account.CompanyId} » : secret de webhook absent — résolution bloquée. "
                + "Saisissez le secret de webhook du compte de signature.");
        }

        // Déchiffrement EN MÉMOIRE uniquement (jamais journalisé — CLAUDE.md n°10).
        var apiKey = _secretProtector.Unprotect(encryptedApiKey);
        var webhookSecret = _secretProtector.Unprotect(encryptedWebhookSecret);

        return new YousignAccountConfig(environment, apiKey, webhookSecret);
    }
}
