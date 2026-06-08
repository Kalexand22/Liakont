namespace Liakont.Console.Api.Tests.Integration;

using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Schéma d'authentification déterministe pour les tests d'intégration HTTP de la console. Remplace
/// l'IdP (Keycloak) afin d'exercer les endpoints SANS conteneur Keycloak : l'identité de l'utilisateur
/// est portée par l'en-tête <c>X-Test-User</c> (un GUID d'utilisateur).
/// <para>
/// L'AUTORISATION reste celle de production : <c>PermissionAuthorizationHandler</c> interroge la base du
/// tenant (<c>identity.grants</c>) pour l'utilisateur authentifié. C'est ce qui rend fidèle le test
/// « 403 sans <c>liakont.read</c> » — on ne court-circuite jamais la décision d'autorisation, on ne fait
/// que fournir une identité de test à la place du flux OIDC.
/// </para>
/// </summary>
public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>Nom du schéma d'authentification de test.</summary>
    public const string SchemeName = "Test";

    /// <summary>En-tête portant le GUID de l'utilisateur de test à authentifier.</summary>
    public const string UserHeader = "X-Test-User";

    /// <summary>
    /// En-tête OPTIONNEL portant le GUID de la société (company_id) de l'utilisateur. En production, le
    /// jeton Keycloak porte ce claim (mappé depuis l'attribut utilisateur <c>company_id</c>,
    /// realm-export.json) ; le harness le fournit ici pour les endpoints scopés par société (table TVA,
    /// API04 — résolus via <c>ICompanyFilter</c>/<c>IActorContext.CompanyId</c>).
    /// </summary>
    public const string CompanyHeader = "X-Test-Company";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var headerValues))
        {
            // Aucune identité fournie : requête anonyme → 401 sur un endpoint protégé.
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var userId = headerValues.ToString();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };

        // company_id : présent en production (claim Keycloak) ; fourni ici pour les endpoints scopés société.
        if (Request.Headers.TryGetValue(CompanyHeader, out var companyValues)
            && !string.IsNullOrWhiteSpace(companyValues.ToString()))
        {
            claims.Add(new Claim("company_id", companyValues.ToString()));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
