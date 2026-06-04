namespace Liakont.Host.Startup;

using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Asp.Versioning;
using Liakont.Host.AgentApi;
using Liakont.Host.Behaviors;
using Liakont.Host.Components;
using Liakont.Host.Localization;
using Liakont.Host.MultiTenancy;
using Liakont.Host.Navigation;
using Liakont.Host.Security;
using Liakont.Host.Security.Abstractions;
using Liakont.Host.Security.Keycloak;
using Liakont.Host.Services;
using Liakont.Modules.Documents.Infrastructure;
using Liakont.Modules.Ingestion.Application;
using Liakont.Modules.Ingestion.Infrastructure;
using Liakont.Modules.TenantSettings.Infrastructure;
using Liakont.Modules.TvaMapping.Infrastructure;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.Infrastructure.Actions;
using Stratum.Common.Infrastructure.Audit;
using Stratum.Common.Infrastructure.Collaboration;
using Stratum.Common.Infrastructure.CrossTenant;
using Stratum.Common.Infrastructure.Database;
using Stratum.Common.Infrastructure.DataIsolation;
using Stratum.Common.Infrastructure.Events;
using Stratum.Common.Infrastructure.Gis;
using Stratum.Common.Infrastructure.GridPreferences;
using Stratum.Common.Infrastructure.HealthChecks;
using Stratum.Common.Infrastructure.Http;
using Stratum.Common.Infrastructure.Jobs;
using Stratum.Common.Infrastructure.UiRules;
using Stratum.Common.Infrastructure.Validation;
using Stratum.Common.UI;
using Stratum.Common.UI.Models;
using Stratum.Modules.Audit.Infrastructure;
using Stratum.Modules.Audit.Web;
using Stratum.Modules.Identity.Application;
using Stratum.Modules.Identity.Contracts.Queries;
using Stratum.Modules.Identity.Infrastructure;
using Stratum.Modules.Identity.Web;
using Stratum.Modules.Job.Infrastructure;
using Stratum.Modules.Job.Web;
using Stratum.Modules.Notification.Contracts.DTOs;
using Stratum.Modules.Notification.Infrastructure;
using Stratum.Modules.Notification.Infrastructure.Handlers.Jobs;
using Stratum.Modules.Notification.Web;

/// <summary>
/// Centralises service registration, middleware configuration, and endpoint mapping
/// so that both the production entry-point (<c>Program.cs</c>) and E2E tests can
/// share the same application setup over different hosting strategies
/// (Kestrel in production, Kestrel-on-dynamic-port in tests).
/// </summary>
public static class AppBootstrap
{
    /// <summary>Registers all application services on the given builder.</summary>
    public static void ConfigureServices(WebApplicationBuilder builder)
    {
        // Localization
        builder.Services.AddLocalization();

        // Localized tab title provider — registered before AddCommonUI so TryAdd doesn't override.
        builder.Services.AddScoped<Stratum.Common.UI.Services.ITabTitleProvider, LocalizedTabTitleProvider>();

        // Common UI services (IToastService, IConnectionStatusService)
        builder.Services.AddCommonUI(builder.Configuration);

        // Infrastructure
        builder.Services.AddStratumDatabase(builder.Configuration);
        builder.Services.AddStratumEvents();
        builder.Services.AddStratumAudit();
        builder.Services.AddStratumHealthChecks();
        builder.Services.AddGridPreferences();
        builder.Services.AddStratumCompanyFilter();
        builder.Services.AddStratumMultiTenancy(builder.Configuration);
        builder.Services.AddStratumCollaboration();
        builder.Services.AddCrossTenantPublisher();
        builder.Services.AddCrossTenantDispatcher(builder.Configuration);
        builder.Services.AddCrossTenantHandlers(typeof(Stratum.Common.Infrastructure.CrossTenant.TestPing.InboundPingHandler).Assembly);
        builder.Services.AddStratumActionPipeline();
        builder.Services.AddStratumValidationEngine();
        builder.Services.AddStratumUiRuleEngine();
        builder.Services.AddStratumGis(builder.Configuration);

        // Multi-tenant job runner (SOL06) — fans an ITenantJob out over all active tenants.
        // Requires ITenantScopeFactory (registered by AddStratumMultiTenancy above).
        builder.Services.AddTenantJobs();

        // Modules
        builder.Services.AddIdentityModule(builder.Configuration);
        builder.Services.AddJobModule(builder.Configuration);
        builder.Services.AddNotificationModule();
        builder.Services.AddJobHandler<EmailSendJobPayload, EmailSendJobHandler>();
        builder.Services.AddJobHandler<DeliveryRetryJobPayload, DeliveryRetryJobHandler>();
        builder.Services.AddAuditModule();
        builder.Services.AddTenantSettingsModule();
        builder.Services.AddIngestionModule();
        builder.Services.AddTvaMappingModule();

        // Documents après Ingestion (ordre sans impact sur la correction : Ingestion utilise TryAdd,
        // Documents utilise Replace — la vraie implémentation gagne toujours). Conservé après Ingestion
        // pour la lisibilité du registre.
        builder.Services.AddDocumentsModule();

        // Stockage des PDF reçus (PIV04) : chemin racine = PARAMÉTRAGE de déploiement (jamais en dur,
        // CLAUDE.md n°7). Lié depuis la config ; à défaut, repli sous le content root de l'instance.
        builder.Services.Configure<IngestionStorageOptions>(
            builder.Configuration.GetSection(IngestionStorageOptions.SectionName));
        builder.Services.PostConfigure<IngestionStorageOptions>(opts =>
        {
            if (string.IsNullOrWhiteSpace(opts.PdfRootPath))
            {
                opts.PdfRootPath = System.IO.Path.Combine(builder.Environment.ContentRootPath, "App_Data", "ingestion-pdf");
            }
        });

        // Rate limiting de l'API agent (F12 §3.3) — défense en profondeur, PROTECTION ANTI-FLOOD : le
        // vrai rempart contre le brute force est la clé cryptographique (secret 256 bits) + la
        // révocation ; un secret ne se devine pas par volume de requêtes. La fenêtre fixe par IP est
        // donc dimensionnée GÉNÉREUSEMENT pour ne jamais rejeter du trafic légitime (heartbeats,
        // configuration), même agrégé derrière un proxy — un 429 sur un heartbeat légitime
        // déclencherait un FAUX POSITIF du dead-man's switch (F12 §5).
        // NOTE déploiement : derrière le reverse proxy de l'appliance (F12 §6.2/6.6), RemoteIpAddress
        // est l'IP du proxy tant que ForwardedHeaders n'est pas configuré → la fenêtre dégrade en
        // throttle GLOBAL plutôt que par-IP. Activer ForwardedHeaders relève d'OPS et EXIGE une liste
        // de proxys de confiance (sinon X-Forwarded-For est usurpable et la limite contournable).
        // NOTE PIV04 : l'ingestion de documents par lots (gros débit) ajoutera SA PROPRE policy
        // dimensionnée pour le débit, plutôt que de partager ce quota anti-flood.
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(AgentApiEndpoints.RateLimiterPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 600,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));

            // Policy d'INGESTION (PIV04) : le drainage d'un backlog pousse des lots en rafale ; la
            // limite est dimensionnée pour le DÉBIT (et non l'anti-flood) afin de ne jamais rejeter un
            // drainage légitime. Le vrai rempart anti-brute-force reste la clé cryptographique + le
            // filtre d'authentification (déjà appliqués au groupe). Même réserve « derrière proxy »
            // que la policy anti-flood : sans ForwardedHeaders, la fenêtre dégrade en throttle global.
            options.AddPolicy(AgentApiEndpoints.IngestionRateLimiterPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 1200,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));
        });

        // Le module ERP Party n'est pas vendoré (seul Party.Contracts — décision D1). Identity
        // dépend de IPartyQueries par injection ; Liakont ne lie pas ses utilisateurs à des Party
        // ERP (PartyId toujours null). Shim no-op pour satisfaire la validation du graphe DI.
        builder.Services.AddScoped<Stratum.Modules.Party.Contracts.Queries.IPartyQueries,
            Liakont.Host.Compatibility.NullPartyQueries>();

        // Real claims-based permission service (replaces the socle's test/no-op default).
        builder.Services.AddScoped<IPermissionService, ClaimsPermissionService>();

        // API versioning
        builder.Services.AddApiVersioning(opt =>
        {
            opt.DefaultApiVersion = new ApiVersion(1, 0);
            opt.ReportApiVersions = true;
            opt.ApiVersionReader = new UrlSegmentApiVersionReader();
        });

        // OpenAPI documentation (Development and Test only)
        if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Test"))
        {
            builder.Services.AddOpenApi("v1", options =>
            {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "Liakont API",
                    Version = "v1",
                    Description = "Liakont — passerelle de conformité e-invoicing (API plateforme)",
                };

                var components = document.Components ?? new OpenApiComponents();
                document.Components = components;
                components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "Enter your JWT token",
                };

                return Task.CompletedTask;
            });

            options.AddOperationTransformer((operation, context, _) =>
            {
                // Apply Bearer auth requirement only to endpoints that require authorization
                var metadata = context.Description.ActionDescriptor.EndpointMetadata;
                var allowAnonymous = metadata.OfType<Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute>().Any();
                if (!allowAnonymous)
                {
                    var schemeRef = new OpenApiSecuritySchemeReference("Bearer");
                    operation.Security ??= [];
                    operation.Security.Add(new OpenApiSecurityRequirement
                    {
                        [schemeRef] = new List<string>(),
                    });
                }

                return Task.CompletedTask;
            });
        });
        }

        // Settings
        builder.Services.Configure<KeycloakSettings>(builder.Configuration.GetSection("Keycloak"));
        builder.Services.Configure<AdminSeedOptions>(builder.Configuration.GetSection("AdminSeed"));

        // Actor context
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IActorContextAccessor, HttpActorContextAccessor>();

        // Authorization handler
        builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

        // MediatR pipeline behaviors (order: actor → tenant → action pipeline → entity changed)
        builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ActorContextBehavior<,>));
        builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TenantPropagationBehavior<,>));
        builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ActionPipelineBehavior<,>));
        builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(EntityChangedBehavior<,>));

        // Authentication & Authorization — consommées DERRIÈRE l'abstraction d'IdP (décision D10).
        // Keycloak est UNE implémentation ; une alternative in-process (ex. OpenIddict) se branche
        // ici sans toucher au reste du Host. Aucun appel IdP-spécifique hors de la couche d'auth.
        var keycloakSettings = builder.Configuration.GetSection("Keycloak").Get<KeycloakSettings>()
            ?? throw new InvalidOperationException("Keycloak configuration section is required. Configure Keycloak:Authority and Keycloak:ClientId.");

        // Sélecteur d'IdP (décision D10) : « Identity:Provider » pilote l'implémentation,
        // défaut Keycloak. Une alternative in-process (ex. OpenIddict) se branche ici.
        var providerName = builder.Configuration["Identity:Provider"];
        IIdentityProviderAuthenticator idp = SelectIdentityProvider(providerName, keycloakSettings);
        idp.ValidateConfiguration();
        idp.ConfigureAuthentication(builder);

        builder.Services.AddAuthorization(options =>
        {
            // VolunteerPolicy = access gate (user has volunteer role).
            // Permission restriction for volunteer-only users is enforced separately
            // by VolunteerAuthorizationHandler on PermissionRequirement checks.
            options.AddPolicy(VolunteerPermissions.PolicyName, policy =>
                policy.RequireRole(StratumRoles.Volunteer));
        });
        builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        builder.Services.AddScoped<IAuthorizationHandler, VolunteerAuthorizationHandler>();

        // Navigation providers (sidebar)
        builder.Services.AddSingleton<INavSectionProvider, HostNavSectionProvider>();
        builder.Services.AddSingleton<INavSectionProvider, Stratum.Modules.Identity.Web.IdentityNavSectionProvider>();
        builder.Services.AddSingleton<INavSectionProvider, Stratum.Modules.Identity.Web.SecurityNavSectionProvider>();
        builder.Services.AddSingleton<INavSectionProvider, Stratum.Modules.Notification.Web.NotificationNavSectionProvider>();
        builder.Services.AddSingleton<INavSectionProvider, Stratum.Modules.Audit.Web.AuditNavSectionProvider>();
        builder.Services.AddSingleton<INavSectionProvider, Stratum.Modules.Job.Web.JobNavSectionProvider>();

        // Blazor Server-Side Rendering
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents(options =>
            {
                if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Test"))
                {
                    options.DetailedErrors = true;
                }
            });
        builder.Services.AddSignalR(options =>
        {
            // Allow JS interop responses up to 10 MB (screenshots, screen recordings).
            options.MaximumReceiveMessageSize = 10 * 1024 * 1024;
        });
        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddScoped<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider,
            Microsoft.AspNetCore.Components.Server.ServerAuthenticationStateProvider>();
    }

    /// <summary>
    /// Runs one-time initialisation (database migration and admin seed).
    /// Call after <see cref="WebApplication.Build"/>.
    /// </summary>
    public static async Task InitializeDataAsync(WebApplication app)
    {
        app.MigrateDatabase();
        await MigrateExistingTenantsAsync(app);
        await app.Services.SeedAdminUserAsync();
        await SeedRealmRegistryFromDatabaseAsync(app);
    }

    /// <summary>Configures the HTTP pipeline and maps all endpoints.</summary>
    public static void ConfigureMiddleware(WebApplication app)
    {
        app.UseStratumErrorHandling();
        var contentTypeProvider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
        contentTypeProvider.Mappings[".module"] = "application/javascript";
        app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = contentTypeProvider });

        // Localization middleware — must be before authentication so culture is available for all requests
        app.UseRequestLocalization(new RequestLocalizationOptions
        {
            DefaultRequestCulture = new RequestCulture(SupportedCultures.DefaultCulture),
            SupportedCultures = SupportedCultures.All,
            SupportedUICultures = SupportedCultures.All,
            RequestCultureProviders =
            [
                new CookieRequestCultureProvider { CookieName = ".AspNetCore.Culture" },
            ],
        });

        app.UseAuthentication();
        app.UseStratumMultiTenancy();
        app.UseAuthorization();
        app.UseAntiforgery();
        app.UseRateLimiter();

        // OpenAPI & Swagger UI (Development and Test only)
        if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Test"))
        {
            app.MapOpenApi();

            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/openapi/v1.json", "Liakont API v1");
            });
        }

        app.MapStratumHealthChecks();

        // OIDC auth endpoints — only registered when Keycloak login is active
        // (Authority configured AND UseKeycloak=true), matching the Login.razor guard.
        var kcConfig = app.Configuration.GetSection("Keycloak").Get<KeycloakSettings>();
        if (kcConfig?.IsKeycloakLoginActive is true)
        {
            // Login: lives outside the Blazor SSR pipeline so ChallengeAsync can
            // write the 302 without conflicting with Blazor rendering.
            // CSRF note: the .NET OIDC handler generates a correlation cookie + state
            // parameter on ChallengeAsync and validates them on the callback — login
            // CSRF is mitigated at the framework level.
            app.MapGet("/auth/oidc-login", async (HttpContext ctx, string? returnUrl) =>
            {
                // Clear ALL stale OIDC/auth cookies AND session cookie chunks
                // from previous sessions to prevent "Correlation failed" and
                // chunk mismatch on re-login after logout.
                foreach (var cookie in ctx.Request.Cookies.Keys.ToList())
                {
                    if (cookie.StartsWith(".AspNetCore.", StringComparison.Ordinal)
                        && !cookie.StartsWith(".AspNetCore.Antiforgery.", StringComparison.Ordinal))
                    {
                        ctx.Response.Cookies.Delete(cookie);
                    }

                    // Delete stale session cookie chunks (stratum_sessionC1, C2, ...)
                    if (cookie.StartsWith("stratum_session", StringComparison.Ordinal))
                    {
                        ctx.Response.Cookies.Delete(cookie);
                    }
                }

                await ctx.ChallengeAsync(
                    OpenIdConnectDefaults.AuthenticationScheme,
                    new AuthenticationProperties
                    {
                        RedirectUri = ReturnUrlSanitizer.Sanitize(returnUrl),
                    });
            }).AllowAnonymous();

            // Logout: signs out of OIDC + cookie outside Blazor SSR.
            // Order matters: OIDC sign-out reads the id_token from the cookie first,
            // then cookie sign-out clears the session. Both headers ship in one response.
            app.MapGet("/auth/oidc-logout", async (HttpContext ctx) =>
            {
                var kc = ctx.RequestServices.GetRequiredService<IOptions<KeycloakSettings>>().Value;
                await ctx.SignOutAsync(
                    OpenIdConnectDefaults.AuthenticationScheme,
                    new AuthenticationProperties
                    {
                        RedirectUri = ReturnUrlSanitizer.Sanitize(kc.PostLogoutRedirectUri),
                    });
                await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }).RequireAuthorization();
        }

        // Test-only fallback login: cookie-based auth without Keycloak.
        // Only registered in Test environment with UseKeycloak=false.
        if (app.Environment.IsEnvironment("Test") && kcConfig?.IsKeycloakLoginActive is not true)
        {
            app.MapPost("/auth/test-login", async (HttpContext ctx) =>
            {
                var form = await ctx.Request.ReadFormAsync();
                var username = form["username"].ToString();
                var returnUrl = form["returnUrl"].ToString();

                if (string.IsNullOrWhiteSpace(username))
                {
                    return Results.BadRequest("username is required");
                }

                var identityQueries = ctx.RequestServices.GetRequiredService<IIdentityQueries>();
                var user = await identityQueries.GetUserByUsername(username);
                if (user is null || !user.IsActive)
                {
                    return Results.Redirect("/login?error=invalid");
                }

                var permissions = await identityQueries.GetUserPermissions(user.Id);
                var claims = new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new(ClaimTypes.Name, user.Username),
                    new("preferred_username", user.Username),
                    new(ClaimTypes.Email, user.Email),
                    new("name", user.DisplayName),
                };

                foreach (var role in user.Roles)
                {
                    claims.Add(new Claim(ClaimTypes.Role, role));
                }

                foreach (var perm in permissions)
                {
                    claims.Add(new Claim("permission", perm));
                }

                claims.Add(new Claim("company_id", Guid.Empty.ToString("D")));

                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                await ctx.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(identity));

                var safe = ReturnUrlSanitizer.Sanitize(returnUrl);
                return Results.Redirect(safe);
            }).AllowAnonymous();
        }

        // API versioning — all REST endpoints under /api/v1/...
        var versionSet = app.NewApiVersionSet()
            .HasApiVersion(new ApiVersion(1, 0))
            .Build();

        var v1 = app.MapGroup("/api/v{version:apiVersion}")
            .WithApiVersionSet(versionSet)
            .MapToApiVersion(new ApiVersion(1, 0));

        // Culture switch endpoint
        v1.MapPost("/culture", (HttpContext ctx) =>
        {
            var culture = ctx.Request.Form["culture"].ToString();
            if (string.IsNullOrEmpty(culture) || !SupportedCultures.IsSupported(culture))
            {
                return Results.BadRequest();
            }

            ctx.Response.Cookies.Append(
                ".AspNetCore.Culture",
                CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
                new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true });

            var returnUrl = ctx.Request.Form["returnUrl"].ToString();
            var safe = !string.IsNullOrEmpty(returnUrl)
                && Uri.TryCreate(returnUrl, UriKind.Relative, out _)
                && returnUrl.StartsWith('/')
                && !returnUrl.StartsWith("//", StringComparison.Ordinal)
                && !returnUrl.Contains('\\')
                    ? returnUrl
                    : "/";
            return Results.Redirect(safe);
        }).AllowAnonymous();

        v1.MapIdentityEndpoints();
        v1.MapJobEndpoints();
        v1.MapNotificationEndpoints();
        v1.MapAuditEndpoints();
        v1.MapTenantAdminEndpoints();

        // API agent → plateforme (contrat d'ingestion, F12 §3) : groupe /api/agent/v1 distinct de
        // l'API console OIDC, authentifié par clé API (filtre) et protégé par rate limiting.
        app.MapAgentApi();

        app.MapRazorComponents<App>()
            .AddAdditionalAssemblies(
                typeof(Stratum.Modules.Notification.Web.NotificationEndpointMapping).Assembly,
                typeof(Stratum.Modules.Identity.Web.IdentityEndpointMapping).Assembly,
                typeof(Stratum.Modules.Audit.Web.AuditNavSectionProvider).Assembly,
                typeof(Stratum.Modules.Job.Web.JobNavSectionProvider).Assembly)
            .AddInteractiveServerRenderMode();
    }

    /// <summary>
    /// Applies any missing migrations to all active tenant databases.
    /// Must run after system migrations so that <c>outbox.tenants</c> is available.
    /// </summary>
    private static async Task MigrateExistingTenantsAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var provisioner = scope.ServiceProvider.GetRequiredService<ITenantProvisioningService>();
        await provisioner.MigrateExistingTenantsAsync(app.Lifetime.ApplicationStopping);
    }

    /// <summary>
    /// Sélectionne l'implémentation d'<see cref="IIdentityProviderAuthenticator"/> à utiliser
    /// selon « Identity:Provider » (décision D10). Par défaut Keycloak ; une alternative
    /// in-process (ex. OpenIddict) s'ajoute dans le registre ci-dessous sans toucher au
    /// reste du Host.
    /// </summary>
    private static IIdentityProviderAuthenticator SelectIdentityProvider(
        string? providerName,
        KeycloakSettings keycloakSettings)
    {
        // Registre des fabriques d'IdP, indexé par nom de fournisseur. Une alternative
        // in-process (ex. OpenIddict) s'ajoute ici comme une entrée supplémentaire.
        var providers = new Dictionary<string, Func<IIdentityProviderAuthenticator>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Keycloak"] = () => new KeycloakIdentityProviderAuthenticator(keycloakSettings),
        };

        // Défaut : Keycloak (aucun provider explicite configuré).
        var selected = string.IsNullOrWhiteSpace(providerName) ? "Keycloak" : providerName;

        if (providers.TryGetValue(selected, out var factory))
        {
            return factory();
        }

        // On bloque plutôt que de démarrer avec une authentification incorrecte.
        throw new InvalidOperationException(
            $"Fournisseur d'identité « {providerName} » inconnu. Implémentations disponibles : "
            + $"{string.Join(", ", providers.Keys)}. Branchez une implémentation "
            + "d'IIdentityProviderAuthenticator pour ce fournisseur (décision D10).");
    }

    /// <summary>
    /// Loads existing tenant-realm mappings from the database into the in-memory
    /// <see cref="IRealmRegistry"/> so that JWTs from previously provisioned realms
    /// are accepted without requiring them in static config.
    /// </summary>
    private static async Task SeedRealmRegistryFromDatabaseAsync(WebApplication app)
    {
        var realmRegistry = app.Services.GetRequiredService<IRealmRegistry>();
        var tenantQueries = app.Services.GetRequiredService<ITenantQueries>();
        var kc = app.Configuration.GetSection("Keycloak").Get<KeycloakSettings>();

        if (kc is null || !kc.IsConfigured)
        {
            return;
        }

        var baseUrl = kc.Authority[..kc.Authority.LastIndexOf("/realms/", StringComparison.Ordinal)];
        var tenants = await tenantQueries.ListAsync();

        foreach (var tenant in tenants.Where(t => t.IsActive && t.RealmName is not null))
        {
            var authority = $"{baseUrl}/realms/{tenant.RealmName}";
            realmRegistry.RegisterRealm(tenant.RealmName!, tenant.Id, authority);
        }
    }
}
