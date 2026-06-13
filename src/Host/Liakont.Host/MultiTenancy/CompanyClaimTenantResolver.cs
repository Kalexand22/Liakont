namespace Liakont.Host.MultiTenancy;

using Microsoft.AspNetCore.Http;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Résolveur AUTORITAIRE (ADR-0021 §2c) : dérive le tenant courant du claim <c>company_id</c> du jeton,
/// via <see cref="ICompanyTenantLookup"/> (<c>company_id → outbox.tenants.company_id → tenant</c>).
/// En realm Keycloak unique, c'est la voie autoritaire ; il est enregistré EN TÊTE de la chaîne, avant
/// les voies client-fournies (sous-domaine, header) qui ne sont plus la source autoritaire.
/// </summary>
/// <remarks>
/// <para>
/// Lit le claim DIRECTEMENT depuis le <see cref="System.Security.Claims.ClaimsPrincipal"/> (jamais via
/// <see cref="Stratum.Common.Abstractions.Security.IActorContext"/>) : ce résolveur s'exécute DANS
/// <c>TenantMiddleware</c>, AVANT que <c>MutableTenantContext.TenantId</c> ne soit posé.
/// <c>HttpActorContextAccessor.Build()</c> lit ce <c>TenantId</c> et MET EN CACHE l'acteur ; le lire ici
/// figerait un acteur au <c>TenantId</c> null pour toute la requête. La lecture de <c>company_id</c> via
/// <c>IActorContext</c> relève du cross-check de RLM03 (middleware POSTÉRIEUR à la résolution).
/// </para>
/// <para>
/// Le REJET sur contradiction (un indice client-fourni qui diverge du jeton ⇒ 403) est porté par RLM03 ;
/// ici, l'absence de claim <c>company_id</c> (utilisateur non authentifié, chemin agent <c>X-Agent-Key</c>)
/// fait simplement retomber sur les résolveurs suivants.
/// </para>
/// </remarks>
internal sealed class CompanyClaimTenantResolver : ITenantResolver
{
    internal const string CompanyIdClaimType = "company_id";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ICompanyTenantLookup _companyTenantLookup;

    public CompanyClaimTenantResolver(
        IHttpContextAccessor httpContextAccessor,
        ICompanyTenantLookup companyTenantLookup)
    {
        _httpContextAccessor = httpContextAccessor;
        _companyTenantLookup = companyTenantLookup;
    }

    public string? Resolve()
    {
        var raw = _httpContextAccessor.HttpContext?.User.FindFirst(CompanyIdClaimType)?.Value;
        if (!Guid.TryParse(raw, out var companyId))
        {
            return null;
        }

        return _companyTenantLookup.FindTenantId(companyId);
    }
}
