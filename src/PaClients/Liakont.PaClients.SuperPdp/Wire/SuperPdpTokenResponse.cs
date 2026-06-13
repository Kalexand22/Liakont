namespace Liakont.PaClients.SuperPdp.Wire;

/// <summary>
/// Réponse du token-endpoint OAuth 2.0 de Super PDP (✅ confirmée par le test réel du 2026-06-11 :
/// <c>token_type</c> bearer, <c>expires_in</c> 1799). DTO PROPRIÉTAIRE, <c>internal</c> : aucun type
/// OAuth ne traverse <see cref="Modules.Transmission.Contracts.IPaClient"/> (frontière F14 §3.1/§7).
/// </summary>
internal sealed record SuperPdpTokenResponse
{
    /// <summary>Jeton d'accès bearer à injecter en <c>Authorization: Bearer &lt;token&gt;</c>.</summary>
    public string? AccessToken { get; init; }

    /// <summary>Type de jeton (« bearer »).</summary>
    public string? TokenType { get; init; }

    /// <summary>Durée de vie du jeton en SECONDES (sert au calcul de l'échéance de cache).</summary>
    public int? ExpiresIn { get; init; }
}
