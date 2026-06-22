namespace Liakont.PaClients.ChorusPro.Wire;

/// <summary>
/// Réponse du token-endpoint OAuth 2.0 PISTE (<c>client_credentials</c> + <c>scope=openid</c>, F18 §2.1 —
/// forme OAuth 2.0 RFC 6749 §5.1 : <c>access_token</c> / <c>token_type</c> / <c>expires_in</c>, à confirmer
/// au Swagger PISTE). DTO PROPRIÉTAIRE, <c>internal</c> : aucun type OAuth ne traverse
/// <see cref="Modules.Transmission.Contracts.IPaClient"/> (frontière F18 §2/§7 ; acceptance CP02).
/// </summary>
internal sealed record ChorusProTokenResponse
{
    /// <summary>Jeton d'accès bearer à injecter en <c>Authorization: Bearer &lt;token&gt;</c>.</summary>
    public string? AccessToken { get; init; }

    /// <summary>Type de jeton (« bearer »).</summary>
    public string? TokenType { get; init; }

    /// <summary>
    /// Durée de vie du jeton en SECONDES (sert au calcul de l'échéance de cache). PISTE pilote sur cette
    /// valeur RÉELLE — jamais « 3600 s » figé (F18 §2.1 ; CLAUDE.md n°2).
    /// </summary>
    public int? ExpiresIn { get; init; }
}
