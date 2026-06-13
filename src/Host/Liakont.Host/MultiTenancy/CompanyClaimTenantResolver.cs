namespace Liakont.Host.MultiTenancy;

using System;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
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
/// Le mapping <c>company_id → tenant</c> est quasi immuable (ne change qu'au provisioning) : il est mis en
/// cache mémoire court (TTL 30 s, résultats POSITIFS uniquement) pour ne pas requêter la base système à
/// chaque requête authentifiée. Une erreur de lecture du registre est journalisée et N'AVORTE PAS la
/// chaîne : le résolveur retourne <c>null</c> (voies de repli évaluées) plutôt que de propager
/// l'exception — l'isolation reste gardée par le scoping métier (CLAUDE.md n°9) et, à terme, le
/// cross-check claim-based de RLM03.
/// </para>
/// <para>
/// Le REJET sur contradiction (un indice client-fourni qui diverge du jeton ⇒ 403) est porté par RLM03 ;
/// ici, l'absence de claim <c>company_id</c> (utilisateur non authentifié, chemin agent <c>X-Agent-Key</c>)
/// fait simplement retomber sur les résolveurs suivants.
/// </para>
/// </remarks>
internal sealed partial class CompanyClaimTenantResolver : ITenantResolver
{
    internal const string CompanyIdClaimType = "company_id";

    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ICompanyTenantLookup _companyTenantLookup;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CompanyClaimTenantResolver> _logger;

    public CompanyClaimTenantResolver(
        IHttpContextAccessor httpContextAccessor,
        ICompanyTenantLookup companyTenantLookup,
        IMemoryCache cache,
        ILogger<CompanyClaimTenantResolver> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _companyTenantLookup = companyTenantLookup;
        _cache = cache;
        _logger = logger;
    }

    public string? Resolve()
    {
        var raw = _httpContextAccessor.HttpContext?.User.FindFirst(CompanyIdClaimType)?.Value;
        if (!Guid.TryParse(raw, out var companyId))
        {
            return null;
        }

        var cacheKey = CacheKey(companyId);
        if (_cache.TryGetValue(cacheKey, out string? cachedTenantId))
        {
            return cachedTenantId;
        }

        string? tenantId;
        try
        {
            tenantId = _companyTenantLookup.FindTenantId(companyId);
        }
        catch (Exception ex)
        {
            // Échec de lecture du registre système : journaliser et NE PAS avorter la chaîne de
            // résolution (le résolveur ne peut pas conclure ⇒ null ⇒ voies de repli évaluées).
            LogLookupFailed(_logger, companyId, ex);
            return null;
        }

        // Ne cacher que les résultats POSITIFS : un company_id sans tenant (jeton inconnu, tenant
        // fraîchement provisionné) doit pouvoir être re-résolu sans attendre l'expiration.
        if (tenantId is not null)
        {
            _cache.Set(cacheKey, tenantId, CacheTtl);
        }

        return tenantId;
    }

    private static string CacheKey(Guid companyId) => $"company-tenant:{companyId}";

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Résolution du tenant par company_id '{CompanyId}' impossible (lecture du registre en échec) — chaîne de repli évaluée.")]
    private static partial void LogLookupFailed(ILogger logger, Guid companyId, Exception exception);
}
