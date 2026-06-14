namespace Liakont.Host.MultiTenancy;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Liakont.Host.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Common.Abstractions.Security;

/// <summary>
/// Cross-check d'isolation par claim <c>company_id</c> (ADR-0021 §2b, item RLM03, INV-0021-4) : middleware
/// <b>GLOBAL fail-closed</b> qui matérialise la défense en profondeur remplaçant la frontière cryptographique
/// par-realm. En realm Keycloak unique, ce qui distingue le tenant A du tenant B n'est plus la clé de
/// signature du realm mais la <b>valeur d'un claim</b> + la <b>discipline de ce cross-check</b> — d'où son
/// caractère global et bloquant.
/// <para>
/// Pour TOUTE requête authentifiée d'un <b>utilisateur de tenant</b> (principal OIDC/cookie), exige :
/// (1) la présence du claim <c>company_id</c> ; (2) que ce <c>company_id</c> résolve, via le registre
/// autoritaire <see cref="ICompanyTenantLookup"/> (<c>company_id → outbox.tenants → tenant</c>, ADR-0021
/// §2c), au tenant <b>servi</b> (<see cref="ITenantContext.TenantId"/>) ; (3) qu'aucun indice de tenant
/// <b>client-fourni délibéré</b> (<see cref="IClientSuppliedTenantResolver"/> : en-tête <c>X-Tenant-Id</c>
/// — le sous-domaine est exclu en SaaS mutualisé mono-host) ne <b>contredise</b> le jeton. Absence de
/// claim, divergence ou contradiction ⇒ <b>403</b>, jamais « servir quand même ».
/// </para>
/// <para>
/// Hors périmètre : requêtes <b>anonymes</b> (login, page de suspension, fichiers statiques) ; le
/// <b>super-admin d'instance</b> (cross-tenant légitime, sans <c>company_id</c> — même court-circuit que
/// <see cref="TenantSuspensionMiddleware"/>) ; le <b>chemin agent</b> qui n'est PAS authentifié
/// cookie/OIDC à ce stade (sa clé <c>X-Agent-Key</c> est validée par <c>AgentApiAuthenticationFilter</c>
/// POSTÉRIEUR) — la garde <c>IsAuthenticated</c> l'écarte sans nécessiter d'exemption par en-tête. Inséré
/// APRÈS <c>UseAuthentication</c> +
/// <c>UseStratumMultiTenancy</c> (principal authentifié, tenant résolu) et AVANT <c>UseAuthorization</c>.
/// Le contrôle PRIMAIRE d'isolation reste le scoping métier des requêtes (CLAUDE.md n°9), inchangé.
/// </para>
/// <para>
/// <c>company_id</c> est lu via l'abstraction d'acteur <see cref="IActorContext"/> — jamais via un type
/// Keycloak (INV-0021-9, garde NetArchTest sur le namespace <c>Liakont.Host.MultiTenancy</c>).
/// </para>
/// </summary>
internal sealed class TenantCompanyCrossCheckMiddleware
{
    private const string NoCompanyMessage =
        "Accès refusé : votre session ne porte pas d'identifiant de société. Reconnectez-vous ; si le problème persiste, contactez votre opérateur Liakont.";

    private const string UnknownCompanyMessage =
        "Accès refusé : votre société n'est rattachée à aucun espace de travail. Contactez votre opérateur Liakont.";

    private const string IsolationMismatchMessage =
        "Accès refusé : incohérence d'isolation entre votre session et l'espace demandé. Contactez votre opérateur Liakont.";

    private readonly RequestDelegate _next;

    public TenantCompanyCrossCheckMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ITenantContext tenantContext,
        IActorContextAccessor actorContextAccessor,
        ICompanyTenantLookup companyTenantLookup,
        IMemoryCache cache,
        IEnumerable<IClientSuppliedTenantResolver> clientSuppliedResolvers)
    {
        // Hors périmètre : requête anonyme (le principal OIDC/cookie n'est pas établi). Le chemin agent
        // n'est PAS authentifié cookie/OIDC à ce stade — sa clé X-Agent-Key est validée par un filtre
        // d'endpoint POSTÉRIEUR — la garde IsAuthenticated l'écarte donc naturellement. NE PAS exempter sur
        // la présence d'un en-tête non validé : un principal authentifié pourrait sinon s'auto-exempter en
        // ajoutant X-Agent-Key (bypass du cross-check).
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        // Super-admin d'instance : accès cross-tenant légitime, sans company_id (même court-circuit que
        // TenantSuspensionMiddleware). Sans cette exemption, le cross-check contredirait l'invariant
        // « super-admin hors périmètre tenant ».
        if (SuperAdminRoles.IsSuperAdmin(context.User))
        {
            await _next(context);
            return;
        }

        // Utilisateur de TENANT à partir d'ici. Fail-closed.
        var companyId = actorContextAccessor.Current.CompanyId;
        if (companyId is null)
        {
            await DenyAsync(context, NoCompanyMessage);
            return;
        }

        // Voie AUTORITAIRE (§2c) : le tenant DÉRIVE du jeton (company_id → outbox.tenants → tenant). On le
        // re-dérive ICI indépendamment du tenant « servi » — sinon un indice client-fourni qui aurait piloté
        // la résolution (résolveur jeton en échec, registre indisponible) serait accepté en silence.
        // Réutilise le cache 30 s de CompanyClaimTenantResolver (même clé) : pas de round-trip base système
        // redondant sur le chemin chaud. Fail-CLOSED (403) sur une erreur transitoire du registre : jamais
        // une rafale de 500 ne doit passer en lieu et place d'un refus sécurisé.
        var cacheKey = CompanyClaimTenantResolver.CacheKey(companyId.Value);
        if (!cache.TryGetValue(cacheKey, out string? tokenTenantId))
        {
            try
            {
                tokenTenantId = companyTenantLookup.FindTenantId(companyId.Value);
            }
            catch (Exception)
            {
                // Aléa transitoire du registre système : fail-CLOSED (403), jamais une 500 en rafale.
                tokenTenantId = null;
            }

            // Ne cacher que les résultats POSITIFS (cohérent avec CompanyClaimTenantResolver).
            if (tokenTenantId is not null)
            {
                cache.Set(cacheKey, tokenTenantId, CompanyClaimTenantResolver.CacheTtl);
            }
        }

        if (string.IsNullOrEmpty(tokenTenantId))
        {
            await DenyAsync(context, UnknownCompanyMessage);
            return;
        }

        // Le tenant SERVI (résolu par la chaîne) doit être EXACTEMENT celui du jeton.
        if (!TenantIdEquals(tenantContext.TenantId, tokenTenantId))
        {
            await DenyAsync(context, IsolationMismatchMessage);
            return;
        }

        // Belt-and-suspenders (§2c) : un indice de tenant CLIENT-FOURNI DÉLIBÉRÉ (en-tête X-Tenant-Id ; le
        // sous-domaine est exclu en mono-host) qui CONTREDIT le jeton ⇒ 403 — jamais servi silencieusement
        // comme tenant du jeton. Garde une
        // réintroduction future d'un en-tête « de confiance » de fuir en silence (c'est ce 403-sur-
        // contradiction qu'assert le test d'INV-0021-4).
        foreach (var resolver in clientSuppliedResolvers)
        {
            var hint = resolver.Resolve();
            if (hint is not null && !TenantIdEquals(hint, tokenTenantId))
            {
                await DenyAsync(context, IsolationMismatchMessage);
                return;
            }
        }

        await _next(context);
    }

    private static bool TenantIdEquals(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static async Task DenyAsync(HttpContext context, string message)
    {
        // 403 fail-closed pour TOUTES les routes (API et UI) : un échec de cross-check est une ANOMALIE
        // (altération / mauvaise configuration), pas un état opérationnel attendu — pas de page dédiée ni de
        // redirection (anti-boucle). Message opérateur en français avec action corrective (CLAUDE.md n°12).
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsync(
            "{\"message\":\"" + message + "\"}",
            context.RequestAborted);
    }
}
