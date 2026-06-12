namespace Liakont.PaClients.SuperPdp;

/// <summary>
/// Fournit le jeton d'accès OAuth 2.0 (client credentials) du compte Super PDP, en cache et renouvelé
/// avant expiration (F14 §3.1). Abstraction INTERNE au plug-in : aucun type OAuth ne traverse
/// <see cref="Modules.Transmission.Contracts.IPaClient"/> (frontière F14 §7). Le découplage permet aussi
/// au <see cref="SuperPdpClient"/> d'être testé avec un fournisseur de jeton fixe (sans aller-retour
/// OAuth réseau), et au <see cref="SuperPdpTokenProvider"/> d'être testé séparément (cache / refresh /
/// invalidation sur 401).
/// </summary>
internal interface ISuperPdpTokenProvider
{
    /// <summary>
    /// Retourne un jeton d'accès valide. Renouvelle le jeton s'il est expiré (ou absent), ou si
    /// <paramref name="forceRefresh"/> est vrai (utilisé après un <c>401</c> : le jeton en cache est
    /// peut-être révoqué/expiré côté PA — on en redemande un avant de conclure à une erreur d'auth).
    /// </summary>
    /// <param name="forceRefresh">Vrai = ignorer le cache et redemander un jeton.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken);
}
