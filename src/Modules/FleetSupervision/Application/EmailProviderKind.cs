namespace Liakont.Modules.FleetSupervision.Application;

/// <summary>
/// Mode d'authentification du fournisseur d'envoi d'emails d'instance (ADR-0039). Le comportement du
/// transport est piloté par ce <em>kind</em> déclaré (jamais un <c>if (provider is X)</c> produit) : SMTP
/// basic-auth classique, ou XOAUTH2 (Gmail / Office 365) via MailKit natif. Stocké par son NOM en base
/// (robuste à un renumérotage — convention du dépôt, cf. <c>fleet.instances</c>).
/// </summary>
public enum EmailProviderKind
{
    /// <summary>SMTP + authentification basic (utilisateur / mot de passe). Repli et cas historique (ADR-0018).</summary>
    SmtpBasic = 0,

    /// <summary>Gmail via SMTP XOAUTH2 (<c>smtp.gmail.com</c>). Jeton d'accès obtenu par rafraîchissement OAuth2.</summary>
    GoogleOAuth2 = 1,

    /// <summary>Office 365 via SMTP XOAUTH2 (<c>smtp.office365.com</c>). Jeton d'accès obtenu par rafraîchissement OAuth2.</summary>
    MicrosoftOAuth2 = 2,
}
