namespace Liakont.Host.InstanceEmail;

/// <summary>
/// Purposes de chiffrement Data Protection dédiés aux secrets d'envoi d'emails d'instance (ADR-0039),
/// miroir de <c>PaAccountSecretPurposes</c>. Un purpose DISTINCT par secret = isolation cryptographique
/// (un texte chiffré sous un purpose ne se déchiffre pas sous un autre). Les chaînes sont versionnées
/// <c>.v1</c> et IMMUABLES une fois en service (rotation future = suffixe <c>.v2</c> + migration, jamais
/// une édition de la constante). Consommés UNIQUEMENT côté Host (monopole chiffrement/déchiffrement,
/// CLAUDE.md n°6/14) — jamais dans le module de persistance (qui ne voit que du ciphertext).
/// </summary>
public static class EmailSecretPurposes
{
    /// <summary>Purpose du mot de passe SMTP (auth <c>SmtpBasic</c>).</summary>
    public const string SmtpPassword = "Liakont.Host.InstanceEmail.SmtpPassword.v1";

    /// <summary>Purpose du « client_secret » OAuth2 (auth <c>GoogleOAuth2</c> / <c>MicrosoftOAuth2</c>).</summary>
    public const string OAuthClientSecret = "Liakont.Host.InstanceEmail.OAuthClientSecret.v1";

    /// <summary>Purpose du « refresh_token » OAuth2 (échangé contre un access_token à l'envoi).</summary>
    public const string OAuthRefreshToken = "Liakont.Host.InstanceEmail.OAuthRefreshToken.v1";
}
