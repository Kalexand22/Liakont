namespace Liakont.Host.MultiTenancy;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Common.Infrastructure.Database;

public static class MultiTenantServiceCollectionExtensions
{
    /// <summary>
    /// Registers multi-tenant services: tenant context, resolver chain, and composite resolver.
    /// Resolution order: company_id claim (authoritative, ADR-0021 §2c) > subdomain > X-Tenant-Id header
    /// > OIDC issuer > tenant_id JWT claim.
    /// </summary>
    public static IServiceCollection AddStratumMultiTenancy(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Options (bound from "MultiTenancy" section)
        services.Configure<MultiTenancyOptions>(
            configuration.GetSection(MultiTenancyOptions.SectionName));

        // HttpContextAccessor (required by resolver implementations; TryAdd — safe if already registered)
        services.AddHttpContextAccessor();

        // Tenant context (mutable, scoped) — set by middleware, read by consumers
        services.AddScoped<MutableTenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<MutableTenantContext>());

        // Authoritative token→tenant lookup (ADR-0021 §2c) : company_id(jeton) → outbox.tenants → tenant.
        // Sans état (chaîne de connexion système figée) → Singleton.
        services.AddSingleton<ICompanyTenantLookup, CompanyTenantLookup>();

        // Cache du résolveur company_id→tenant (mapping quasi immuable). AddMemoryCache est idempotent
        // (TryAdd) — sûr même si déjà enregistré ailleurs (TenantSuspensionLookup).
        services.AddMemoryCache();

        // Resolver chain — order matters: first registered = highest priority.
        // CompanyClaimTenantResolver EN PREMIER : voie jeton autoritaire en realm unique (ADR-0021 §2c) ;
        // les voies client-fournies (sous-domaine, header) ne sont plus la source autoritaire.
        services.AddScoped<ITenantResolver, CompanyClaimTenantResolver>();
        services.AddScoped<ITenantResolver, SubdomainTenantResolver>();
        services.AddScoped<ITenantResolver, HeaderTenantResolver>();
        services.AddScoped<ITenantResolver, OidcIssuerTenantResolver>();
        services.AddScoped<ITenantResolver, JwtClaimTenantResolver>();

        // Composite resolver (evaluates chain in order)
        services.AddScoped<CompositeTenantResolver>();

        // Circuit handler — propagates tenant from HTTP context to Blazor circuit scope.
        // Without this, InteractiveServer components would always hit the system DB.
        services.AddScoped<TenantCircuitHandler>();
        services.AddScoped<CircuitHandler>(sp => sp.GetRequiredService<TenantCircuitHandler>());

        // Tenant scope factory — lets background multi-tenant jobs (TenantJobRunner, SOL06)
        // establish a tenant on a fresh DI scope, the same way the middleware does for HTTP.
        services.AddSingleton<ITenantScopeFactory, TenantScopeFactory>();

        return services;
    }

    /// <summary>
    /// Adds the tenant resolution middleware to the pipeline.
    /// Must be called after <c>UseAuthentication()</c> and before endpoint mapping.
    /// </summary>
    public static IApplicationBuilder UseStratumMultiTenancy(this IApplicationBuilder app)
    {
        return app.UseMiddleware<TenantMiddleware>();
    }
}
