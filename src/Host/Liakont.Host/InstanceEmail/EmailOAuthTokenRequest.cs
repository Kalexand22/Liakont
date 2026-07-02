namespace Liakont.Host.InstanceEmail;

using System.Text;
using Liakont.Modules.FleetSupervision.Application;

/// <summary>
/// Paramètres d'échange <c>refresh_token → access_token</c> pour l'envoi SMTP XOAUTH2 (ADR-0039). Transport
/// MÉMOIRE des secrets en clair (client_secret / refresh_token déchiffrés par le Host) : ces valeurs ne sont
/// JAMAIS journalisées (CLAUDE.md n°10/18) ni persistées en clair — elles vivent le temps de l'appel token.
/// </summary>
public sealed record EmailOAuthTokenRequest
{
    /// <summary>Fournisseur ciblé (détermine l'endpoint token : Google vs Microsoft).</summary>
    public required EmailProviderKind Kind { get; init; }

    /// <summary>« client_id » de l'application OAuth2 (non-secret).</summary>
    public required string ClientId { get; init; }

    /// <summary>« client_secret » de l'application OAuth2 (secret en clair, mémoire seulement).</summary>
    public required string ClientSecret { get; init; }

    /// <summary>« refresh_token » obtenu au consentement initial (secret en clair, mémoire seulement).</summary>
    public required string RefreshToken { get; init; }

    /// <summary>« tenant_id » de l'annuaire Microsoft ; <c>null</c>/vide → <c>common</c> (ignoré pour Google).</summary>
    public string? TenantId { get; init; }

    // Le ToString() synthétisé d'un record imprimerait TOUS les membres — dont les secrets. On REDACTE
    // client_secret / refresh_token (CLAUDE.md n°10/18 : aucun secret ne doit fuir, même par un log accidentel).
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Kind = ").Append(Kind)
            .Append(", ClientId = ").Append(ClientId)
            .Append(", TenantId = ").Append(TenantId ?? "null")
            .Append(", ClientSecret = ***, RefreshToken = ***");
        return true;
    }
}
