namespace Liakont.Host.InstanceEmail;

using System.Text;

/// <summary>
/// Entrée d'enregistrement de la configuration email d'instance (ADR-0039). Les secrets sont transmis EN CLAIR
/// (chiffrés par le service, jamais journalisés) ; un secret <c>null</c>/vide CONSERVE le ciphertext existant
/// (lit-puis-conserve — les champs masqués non re-saisis n'écrasent pas le secret par du vide, ADR-0039 §5).
/// </summary>
public sealed record InstanceEmailConfigInput
{
    /// <summary>Nom du mode d'authentification (<c>SmtpBasic</c> / <c>GoogleOAuth2</c> / <c>MicrosoftOAuth2</c>).</summary>
    public required string Kind { get; init; }

    public required string Host { get; init; }

    public required int Port { get; init; }

    public required bool UseStartTls { get; init; }

    public required string FromAddress { get; init; }

    public required string FromName { get; init; }

    public required string Username { get; init; }

    /// <summary>Mot de passe SMTP en clair ; <c>null</c>/vide = inchangé.</summary>
    public string? SmtpPassword { get; init; }

    /// <summary>« client_id » OAuth2 (non-secret) ; <c>null</c>/vide = non renseigné.</summary>
    public string? OAuthClientId { get; init; }

    /// <summary>« tenant_id » OAuth2 Microsoft (non-secret) ; <c>null</c>/vide = non renseigné.</summary>
    public string? OAuthTenantId { get; init; }

    /// <summary>« client_secret » OAuth2 en clair ; <c>null</c>/vide = inchangé.</summary>
    public string? OAuthClientSecret { get; init; }

    /// <summary>« refresh_token » OAuth2 en clair ; <c>null</c>/vide = inchangé.</summary>
    public string? OAuthRefreshToken { get; init; }

    /// <summary>Ligne active et autoritaire (l'emporte sur le repli <c>appsettings</c>).</summary>
    public required bool Enabled { get; init; }

    // ToString() synthétisé REDACTÉ : ce record transporte des secrets EN CLAIR (mot de passe / client_secret /
    // refresh_token) — ils ne doivent jamais fuir par un log/interpolation accidentel (CLAUDE.md n°10/18).
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Kind = ").Append(Kind)
            .Append(", Host = ").Append(Host)
            .Append(", Port = ").Append(Port)
            .Append(", Enabled = ").Append(Enabled)
            .Append(", SmtpPassword = ***, OAuthClientSecret = ***, OAuthRefreshToken = ***");
        return true;
    }
}
