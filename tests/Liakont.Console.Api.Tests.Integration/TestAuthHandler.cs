namespace Liakont.Console.Api.Tests.Integration;

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Modules.Identity.Contracts.Queries;

/// <summary>
/// Schéma d'authentification déterministe pour les tests d'intégration HTTP de la console. Remplace
/// l'IdP (Keycloak) afin d'exercer les endpoints SANS conteneur Keycloak : l'identité de l'utilisateur
/// est portée par l'en-tête <c>X-Test-User</c> (un GUID d'utilisateur).
/// <para>
/// L'AUTORISATION reste celle de production : la garde (<c>PermissionAuthorizationHandler</c>) lit les
/// claims <c>permission</c> du principal (mécanisme unique, ADR-0017). Ce harness projette donc les
/// permissions de l'utilisateur en claims <c>permission</c> — exactement comme la projection au sign-in
/// OIDC en production. La SOURCE de ces permissions reste la base du tenant (<c>identity.grants</c>) : la
/// décision d'autorisation demeure fidèle à la production (DB), simplement transportée en claims comme
/// sous OIDC. C'est ce qui rend fidèle le test « 403 sans <c>liakont.read</c> » — on ne court-circuite
/// jamais la décision, on fournit une identité de test à la place du flux OIDC.
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

    /// <summary>
    /// En-tête de résolution du tenant (production : <c>UseStratumMultiTenancy</c>). Sert ici à projeter
    /// les permissions de l'utilisateur (grants de CE tenant) en claims, comme la projection au sign-in.
    /// </summary>
    public const string TenantHeader = "X-Tenant-Id";

    /// <summary>
    /// En-tête OPTIONNEL portant les rôles de l'utilisateur (séparés par des virgules), projetés en claims
    /// de rôle. En production ces rôles viennent du jeton de l'IdP ; le harness les fournit ici pour les
    /// endpoints gardés par rôle (ex. <c>/admin/tenants</c> → <c>SystemAdmin</c>, via <c>RequireRole</c>).
    /// La décision d'autorisation reste celle de production (la garde lit le claim) — seule l'identité de
    /// test remplace le flux IdP.
    /// </summary>
    public const string RolesHeader = "X-Test-Roles";

    private const string PermissionClaimType = "permission";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var headerValues))
        {
            // Aucune identité fournie : requête anonyme → 401 sur un endpoint protégé.
            return AuthenticateResult.NoResult();
        }

        var userId = headerValues.ToString();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return AuthenticateResult.NoResult();
        }

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };

        // company_id : présent en production (claim Keycloak) ; fourni ici pour les endpoints scopés société.
        if (Request.Headers.TryGetValue(CompanyHeader, out var companyValues)
            && !string.IsNullOrWhiteSpace(companyValues.ToString()))
        {
            claims.Add(new Claim("company_id", companyValues.ToString()));
        }

        // Projette les permissions de l'utilisateur (grants de CE tenant) en claims "permission", comme
        // la projection au sign-in OIDC : la garde (PermissionAuthorizationHandler) lit ces claims. La
        // décision reste celle de la base (identity.grants), simplement transportée en claims.
        if (Guid.TryParse(userId, out var userGuid)
            && Request.Headers.TryGetValue(TenantHeader, out var tenantValues)
            && !string.IsNullOrWhiteSpace(tenantValues.ToString()))
        {
            var scopeFactory = Context.RequestServices.GetRequiredService<ITenantScopeFactory>();
            await using var scope = scopeFactory.Create(tenantValues.ToString());
            var identityQueries = scope.Services.GetRequiredService<IIdentityQueries>();
            foreach (var permission in await identityQueries.GetUserPermissions(userGuid, Context.RequestAborted))
            {
                claims.Add(new Claim(PermissionClaimType, permission));
            }
        }

        // Rôles (ex. SystemAdmin) projetés en claims de rôle — la garde RequireRole les lit (comme l'IdP).
        if (Request.Headers.TryGetValue(RolesHeader, out var roleValues))
        {
            foreach (var role in roleValues.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return AuthenticateResult.Success(ticket);
    }
}
