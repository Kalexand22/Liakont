namespace Liakont.Host.InstanceEmail;

/// <summary>
/// Modèle de formulaire (liaison bidirectionnelle de la page de configuration email d'instance, ADR-0039).
/// Les champs secrets (<see cref="SmtpPassword"/>, <see cref="OAuthClientSecret"/>,
/// <see cref="OAuthRefreshToken"/>) sont TOUJOURS vides au chargement (jamais de round-trip d'un secret vers
/// le navigateur — patron <c>PaAccount</c>) : laissés vides à l'enregistrement, le secret existant est CONSERVÉ.
/// </summary>
public sealed class InstanceEmailConfigForm
{
    /// <summary>Nom du mode d'authentification (<c>SmtpBasic</c> / <c>GoogleOAuth2</c> / <c>MicrosoftOAuth2</c>).</summary>
    public string Kind { get; set; } = "SmtpBasic";

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;

    public bool UseStartTls { get; set; } = true;

    public string FromAddress { get; set; } = string.Empty;

    public string FromName { get; set; } = "Liakont";

    public string Username { get; set; } = string.Empty;

    /// <summary>Mot de passe SMTP EN CLAIR saisi (vide = inchangé). Jamais pré-rempli.</summary>
    public string SmtpPassword { get; set; } = string.Empty;

    /// <summary>« client_id » OAuth2 (non-secret, pré-rempli).</summary>
    public string OAuthClientId { get; set; } = string.Empty;

    /// <summary>« tenant_id » OAuth2 Microsoft (non-secret, pré-rempli).</summary>
    public string OAuthTenantId { get; set; } = string.Empty;

    /// <summary>« client_secret » OAuth2 EN CLAIR saisi (vide = inchangé). Jamais pré-rempli.</summary>
    public string OAuthClientSecret { get; set; } = string.Empty;

    /// <summary>« refresh_token » OAuth2 EN CLAIR saisi (vide = inchangé). Jamais pré-rempli.</summary>
    public string OAuthRefreshToken { get; set; } = string.Empty;

    /// <summary>Ligne active et autoritaire (l'emporte sur le repli <c>appsettings</c>).</summary>
    public bool Enabled { get; set; }

    /// <summary>Adresse destinataire pour le bouton « Envoyer un email de test » (hors persistance).</summary>
    public string TestRecipient { get; set; } = string.Empty;
}
