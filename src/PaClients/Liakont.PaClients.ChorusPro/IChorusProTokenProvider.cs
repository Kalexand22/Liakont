namespace Liakont.PaClients.ChorusPro;

/// <summary>
/// Fournit le jeton d'accès OAuth 2.0 (<c>client_credentials</c> + <c>scope=openid</c> — spécificité PISTE,
/// F18 §2.1) du compte PISTE, en cache et renouvelé AVANT expiration. Abstraction INTERNE au plug-in :
/// aucun type OAuth ne traverse <see cref="Modules.Transmission.Contracts.IPaClient"/> (frontière F18 §2/§7 ;
/// CLAUDE.md n°6). Le découplage permet de tester le <see cref="ChorusProClient"/> avec un fournisseur de
/// jeton fixe (sans aller-retour OAuth réseau) et le <see cref="ChorusProTokenProvider"/> séparément (cache /
/// renouvellement / re-échange sur 401). Modèle technique : <c>ISuperPdpTokenProvider</c> (PAS).
/// </summary>
internal interface IChorusProTokenProvider
{
    /// <summary>
    /// Retourne un jeton d'accès PISTE valide. Renouvelle le jeton s'il est expiré (ou absent), ou si
    /// <paramref name="forceRefresh"/> est vrai (utilisé après un <c>401</c> : le jeton en cache est
    /// peut-être révoqué/expiré côté PISTE — on en redemande un avant de conclure à une erreur d'auth ;
    /// PISTE n'émet pas de <c>refresh_token</c> → re-échange <c>client_credentials</c>, F18 §2.1).
    /// </summary>
    /// <param name="forceRefresh">Vrai = ignorer le cache et redemander un jeton.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken);
}
