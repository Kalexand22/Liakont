namespace Liakont.Modules.FleetSupervision.Application;

/// <summary>
/// Configuration d'envoi d'emails d'INSTANCE (ligne singleton, base système — ADR-0039). Portée par un
/// module instance-level (FleetSupervision : déjà base système / écriture / émetteur d'emails), JAMAIS par
/// Supervision (tenant-scopé, lecture seule).
/// <para>
/// <strong>Frontière chiffrement (CLAUDE.md n°6/14) :</strong> ce record ne transporte que du <em>ciphertext</em>
/// (secrets déjà chiffrés) ou des non-secrets en clair — JAMAIS un secret en clair. Le chiffrement/déchiffrement
/// est le monopole du Host (<c>ISecretProtector</c>, hors de ce module). Les champs <c>Encrypted*</c> sont
/// opaques ; <c>OAuthClientId</c>/<c>OAuthTenantId</c> sont des identifiants d'application/annuaire (non-secrets,
/// clair assumé — INV-EMAIL-CFG-01).
/// </para>
/// </summary>
public sealed record InstanceEmailConfig
{
    /// <summary>Mode d'authentification déclaré (SMTP basic / Gmail / O365). Pilote l'auth du transport.</summary>
    public required EmailProviderKind Kind { get; init; }

    /// <summary>Hôte SMTP (ex. <c>smtp.gmail.com</c>, <c>smtp.office365.com</c>).</summary>
    public required string Host { get; init; }

    /// <summary>Port SMTP (587 STARTTLS par défaut).</summary>
    public required int Port { get; init; }

    /// <summary>STARTTLS (587) si vrai, sinon SSL à la connexion (465).</summary>
    public required bool UseStartTls { get; init; }

    /// <summary>Adresse d'expéditeur (le branding d'instance peut la surcharger côté transport).</summary>
    public required string FromAddress { get; init; }

    /// <summary>Nom d'expéditeur affiché.</summary>
    public required string FromName { get; init; }

    /// <summary>Identifiant SMTP / adresse de la boîte (utilisateur XOAUTH2).</summary>
    public required string Username { get; init; }

    /// <summary>Mot de passe SMTP CHIFFRÉ (Data Protection) ; <c>null</c> = aucun (INV-EMAIL-CFG-01).</summary>
    public string? EncryptedSmtpPassword { get; init; }

    /// <summary>« client_id » OAuth2 EN CLAIR (identifiant d'application, non-secret).</summary>
    public string? OAuthClientId { get; init; }

    /// <summary>« tenant_id » OAuth2 EN CLAIR (identifiant d'annuaire Microsoft, non-secret ; <c>null</c> pour Google).</summary>
    public string? OAuthTenantId { get; init; }

    /// <summary>« client_secret » OAuth2 CHIFFRÉ ; <c>null</c> = non saisi (INV-EMAIL-CFG-01).</summary>
    public string? EncryptedOAuthClientSecret { get; init; }

    /// <summary>« refresh_token » OAuth2 CHIFFRÉ ; <c>null</c> = non saisi (INV-EMAIL-CFG-01).</summary>
    public string? EncryptedOAuthRefreshToken { get; init; }

    /// <summary>
    /// Ligne active et autoritaire (INV-EMAIL-CFG-03) : quand vrai, cette config l'emporte sur le repli
    /// <c>appsettings</c> (bootstrap). Faux = le transport retombe sur <c>appsettings</c> / no-op.
    /// </summary>
    public required bool Enabled { get; init; }
}
