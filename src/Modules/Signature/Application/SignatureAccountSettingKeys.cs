namespace Liakont.Modules.Signature.Application;

/// <summary>
/// Clés des <c>Settings</c> d'un <c>SignatureProviderAccount</c> (descripteur Contracts) portant la config
/// NON sensible + les secrets CHIFFRÉS (ADR-0029 §6). Partagées entre le store (qui écrit) et le résolveur du
/// Host (qui déchiffre via <c>ISecretProtector</c>). Le descripteur ne porte JAMAIS de secret en clair
/// (CLAUDE.md n°10) — uniquement le texte chiffré opaque.
/// </summary>
public static class SignatureAccountSettingKeys
{
    /// <summary>Environnement déclaré (« Sandbox » / « Production »).</summary>
    public const string Environment = "Environment";

    /// <summary>Identifiants de compte non secrets (JSON opaque, ex. workspace).</summary>
    public const string AccountIdentifiers = "AccountIdentifiers";

    /// <summary>Clé API CHIFFRÉE (texte opaque produit par le coffre du tenant).</summary>
    public const string EncryptedApiKey = "EncryptedApiKey";

    /// <summary>Secret de webhook CHIFFRÉ (texte opaque produit par le coffre du tenant).</summary>
    public const string EncryptedWebhookSecret = "EncryptedWebhookSecret";
}
