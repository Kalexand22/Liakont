namespace Liakont.PaClients.ChorusPro.Tests.Unit;

/// <summary>
/// Fournisseur de jeton OAuth2 PISTE de test : retourne un jeton FIXE sans aller-retour réseau, et compte
/// les appels (et les refresh forcés) pour vérifier que le client demande bien un jeton et qu'un <c>401</c>
/// déclenche un refresh forcé + une seconde tentative (F18 §2.1). Le jeton « rafraîchi » diffère du jeton
/// nominal pour qu'un test distingue les deux sur l'en-tête bearer. Couvre l'acceptance CP03
/// « StubTokenProvider couvre renouvellement + retry 401 ».
/// </summary>
internal sealed class StubChorusProTokenProvider : IChorusProTokenProvider
{
    /// <summary>Jeton bearer nominal (premier échange).</summary>
    public const string NominalToken = "cpro-token";

    /// <summary>Jeton bearer après refresh forcé (sur 401).</summary>
    public const string RefreshedToken = "cpro-token-refreshed";

    /// <summary>Nombre total d'appels à <see cref="GetAccessTokenAsync"/>.</summary>
    public int CallCount { get; private set; }

    /// <summary>Nombre d'appels avec <c>forceRefresh = true</c> (refresh déclenché par un 401).</summary>
    public int ForceRefreshCount { get; private set; }

    /// <inheritdoc />
    public Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        CallCount++;
        if (forceRefresh)
        {
            ForceRefreshCount++;
        }

        return Task.FromResult(forceRefresh ? RefreshedToken : NominalToken);
    }
}
