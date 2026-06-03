namespace Liakont.Tests.E2E;

using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;

/// <summary>
/// <see cref="AuthenticationStateProvider"/> réservé aux tests qui fait le pont entre le rendu
/// SSR et le circuit interactif.
///
/// Problème : le <c>ServerAuthenticationStateProvider</c> par défaut implémente
/// <c>IHostEnvironmentAuthenticationStateProvider</c>. L'infrastructure de circuit appelle
/// <c>SetAuthenticationState</c> sur cette interface, ce qui déclenche
/// <c>NotifyAuthenticationStateChanged</c> avec le principal qu'elle fournit. Le composant
/// <c>CascadingAuthenticationState</c> utilise cette notification directement (et non
/// <c>GetAuthenticationStateAsync</c>), donc même une surcharge de
/// <c>GetAuthenticationStateAsync</c> est court-circuitée. Si le circuit passe un principal
/// non authentifié, <c>AuthorizeRouteView</c> déclenche une redirection vers le login.
///
/// Solution : dériver directement d'<c>AuthenticationStateProvider</c> (et non de
/// <c>ServerAuthenticationStateProvider</c>) SANS implémenter
/// <c>IHostEnvironmentAuthenticationStateProvider</c>. Ainsi l'infrastructure de circuit ne
/// peut pas injecter un mauvais état d'auth ; <c>CascadingAuthenticationState</c> appelle notre
/// <c>GetAuthenticationStateAsync</c>, qui lit <c>IHttpContextAccessor</c> pendant le SSR et
/// retombe sur un cache statique pendant le rendu de circuit.
/// </summary>
internal sealed class E2EAuthenticationStateProvider : AuthenticationStateProvider
{
    private static readonly ConcurrentDictionary<string, ClaimsPrincipal> CachedPrincipals = new();
    private readonly IHttpContextAccessor _accessor;

    public E2EAuthenticationStateProvider(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    /// <summary>
    /// Vide le cache de principal partagé. Appelé en début de chaque test
    /// (<see cref="KeycloakBaseE2ETest.InitializeAsync"/>) pour empêcher qu'un test hérite de
    /// l'état d'authentification du test précédent (la factory app est une collection-fixture
    /// partagée ; le cache statique survivrait sinon d'un test à l'autre).
    /// </summary>
    public static void Reset() => CachedPrincipals.Clear();

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var httpContext = _accessor.HttpContext;

        // Chemin SSR : HttpContext disponible — on met en cache le principal et on le retourne.
        if (httpContext?.User?.Identity?.IsAuthenticated == true)
        {
            CachedPrincipals["e2e"] = httpContext.User;
            return Task.FromResult(new AuthenticationState(httpContext.User));
        }

        // Chemin circuit : pas d'HttpContext — on réutilise le principal du dernier rendu SSR.
        if (CachedPrincipals.TryGetValue("e2e", out var cached))
        {
            return Task.FromResult(new AuthenticationState(cached));
        }

        // Aucun état en cache (le test ne s'est pas encore authentifié).
        return Task.FromResult(new AuthenticationState(new ClaimsPrincipal()));
    }
}
