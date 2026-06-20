namespace Liakont.Host.Security.Keycloak;

using System.IdentityModel.Tokens.Jwt;
using System.Threading.Tasks;
using Liakont.Host.MultiTenancy;
using Liakont.Host.Security;
using Liakont.Host.Security.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Implémentation Keycloak de l'abstraction d'IdP (décision D10) : OIDC (navigateur)
/// + JwtBearer RS256 (API) + cookie, avec support multi-realm via
/// <see cref="RealmRegistry"/> et <see cref="MultiRealmJwksKeyResolver"/>.
/// </summary>
/// <remarks>
/// Keycloak est UNE implémentation de <see cref="IIdentityProviderAuthenticator"/>.
/// Aucune logique spécifique à Keycloak ne doit exister hors de cette couche d'auth.
/// </remarks>
internal sealed class KeycloakIdentityProviderAuthenticator : IIdentityProviderAuthenticator
{
    private readonly KeycloakSettings _settings;

    public KeycloakIdentityProviderAuthenticator(KeycloakSettings settings)
    {
        _settings = settings;
    }

    public string ProviderName => "Keycloak";

    public void ValidateConfiguration()
    {
        if (!_settings.IsConfigured)
        {
            throw new InvalidOperationException(
                "Keycloak:Authority must be configured. Legacy symmetric JWT authentication has been removed.");
        }

        if (!string.IsNullOrEmpty(_settings.PostLogoutRedirectUri)
            && ReturnUrlSanitizer.Sanitize(_settings.PostLogoutRedirectUri) != _settings.PostLogoutRedirectUri)
        {
            throw new InvalidOperationException(
                $"Keycloak:PostLogoutRedirectUri must be a safe relative path starting with '/'. Got: '{_settings.PostLogoutRedirectUri}'");
        }

        // Fenêtre de révocation des permissions sensibles : fail-closed sur une configuration absurde
        // (≤ 0 désactiverait l'atténuation RDF10 et laisserait la fenêtre ≥ 8 h non bornée).
        if (_settings.SensitivePermissionRevocationWindow <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "Keycloak:SensitivePermissionRevocationWindow must be a positive duration "
                + $"(borne la fenêtre de révocation des permissions sensibles). Got: '{_settings.SensitivePermissionRevocationWindow}'");
        }
    }

    /// <summary>
    /// Configures Keycloak OIDC (browser) + RS256 JwtBearer (API) + Cookie + SmartDefault.
    /// Supports multiple realms: the primary realm uses <see cref="KeycloakSettings.Authority"/>,
    /// additional realms are configured via <see cref="KeycloakSettings.AdditionalRealms"/>.
    /// </summary>
    public void ConfigureAuthentication(WebApplicationBuilder builder)
    {
        var kc = _settings;

        // Disable default claim type mapping so OIDC standard names arrive as-is.
        JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

        // Collect all valid issuers and audiences for JwtBearer multi-realm validation
        var validIssuers = new List<string> { kc.Authority };
        var validAudiences = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { kc.ClientId, "account" };
        foreach (var (_, realmConfig) in kc.AdditionalRealms)
        {
            if (!string.IsNullOrWhiteSpace(realmConfig.Authority))
            {
                validIssuers.Add(realmConfig.Authority);
                validAudiences.Add(realmConfig.ClientId);
            }
        }

        var authBuilder = builder.Services.AddAuthentication(options =>
            {
                options.DefaultScheme = "SmartDefault";
                options.DefaultChallengeScheme = "SmartDefault";
            })
            .AddPolicyScheme("SmartDefault", displayName: null, options =>
            {
                options.ForwardDefaultSelector = ctx =>
                {
                    var auth = ctx.Request.Headers[HeaderNames.Authorization].FirstOrDefault();
                    return auth?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) is true
                        ? JwtBearerDefaults.AuthenticationScheme
                        : CookieAuthenticationDefaults.AuthenticationScheme;
                };
            });

        // Primary realm OIDC handler
        authBuilder.AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
        {
            ConfigureOidcOptions(options, kc.Authority, kc.ClientId, kc.ClientSecret, kc.RequireHttpsMetadata, kc.SensitivePermissionRevocationWindow, kc.PostLogoutRedirectUri, kc.MetadataAddress);
        });

        // Additional realm OIDC handlers (named "oidc-{realmName}")
        foreach (var (realmName, realmConfig) in kc.AdditionalRealms)
        {
            if (string.IsNullOrWhiteSpace(realmConfig.Authority))
            {
                continue;
            }

            var schemeName = $"oidc-{realmName}";
            authBuilder.AddOpenIdConnect(schemeName, options =>
            {
                ConfigureOidcOptions(
                    options,
                    realmConfig.Authority,
                    realmConfig.ClientId,
                    realmConfig.ClientSecret,
                    kc.RequireHttpsMetadata,
                    kc.SensitivePermissionRevocationWindow,
                    kc.PostLogoutRedirectUri);
            });
        }

        // JwtBearer accepts tokens from ALL configured realms.
        // Each realm has its own JWKS endpoint; the MultiRealmJwksKeyResolver
        // fetches and caches signing keys from all authorities.
        // Registered as singleton so new realms can be added at runtime via IRealmRegistry.
        var jwksResolver = new MultiRealmJwksKeyResolver(validIssuers, kc.RequireHttpsMetadata);
        builder.Services.AddSingleton(jwksResolver);

        // RealmRegistry: seeded from config, updated at runtime on tenant provisioning.
        // Must be created before JwtBearer so that IssuerValidator can capture it.
        // Uses NullLogger during startup; runtime calls go through proper DI logging.
        var realmRegistry = new RealmRegistry(
            jwksResolver,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RealmRegistry>.Instance);

        // Seed from static config
        foreach (var (realmName, tenantId) in kc.RealmTenantMap)
        {
            var authority = realmName.Equals(
                OidcIssuerTenantResolver.ExtractRealmName(kc.Authority),
                StringComparison.OrdinalIgnoreCase)
                ? kc.Authority
                : $"{kc.Authority[..kc.Authority.LastIndexOf("/realms/", StringComparison.Ordinal)]}/realms/{realmName}";
            realmRegistry.RegisterRealm(realmName, tenantId, authority);
        }

        // Also register additional realms
        foreach (var (realmName, realmConfig) in kc.AdditionalRealms)
        {
            if (!string.IsNullOrWhiteSpace(realmConfig.Authority)
                && kc.RealmTenantMap.TryGetValue(realmName, out var tenantId))
            {
                realmRegistry.RegisterRealm(realmName, tenantId, realmConfig.Authority);
            }
        }

        builder.Services.AddSingleton<IRealmRegistry>(realmRegistry);

        authBuilder.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.RequireHttpsMetadata = kc.RequireHttpsMetadata;
                options.MapInboundClaims = false;

                // Disable automatic metadata discovery (we handle it per-realm via the resolver)
                options.Configuration = new Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration();
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,

                    // Dynamic issuer validation via RealmRegistry instead of static list.
                    // This allows new realms added at runtime to be accepted without restart.
                    IssuerValidator = (issuer, _, _) =>
                    {
                        if (realmRegistry.IsKnownIssuer(issuer))
                        {
                            return issuer;
                        }

                        throw new Microsoft.IdentityModel.Tokens.SecurityTokenInvalidIssuerException(
                            $"IDX10205: Issuer validation failed. Issuer: '{issuer}' is not a known realm.");
                    },
                    ValidateAudience = true,
                    ValidAudiences = validAudiences,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    NameClaimType = "preferred_username",
                    RoleClaimType = "roles",

                    // Custom resolver fetches JWKS from the issuer-specific realm endpoint.
                    // If no keys are resolved (JWKS unreachable or unknown issuer),
                    // the token is rejected because ValidateIssuerSigningKey is true.
                    IssuerSigningKeyResolver = jwksResolver.ResolveSigningKeys,
                };

                // Projette les rôles realm → permissions (matrice §3) en claims "permission" sur le
                // principal du jeton porteur — comme le flux OIDC/cookie. La garde endpoint
                // (PermissionAuthorizationHandler) lit le MÊME claim quel que soit le schéma actif
                // (cookie OU Bearer) : un seul mécanisme d'autorisation, un IdP alternatif réutilise la
                // même projection (INV-IDN01-2/3). Le jeton étant revalidé à chaque requête, la
                // révocation Bearer est honorée à son expiration (pas de fenêtre cookie ici).
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        if (context.Principal?.Identity is System.Security.Claims.ClaimsIdentity identity)
                        {
                            RolePermissionCatalog.ProjectPermissionClaims(identity);
                        }

                        return Task.CompletedTask;
                    },
                };
            })
            .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
            {
                options.LoginPath = "/login";
                options.Cookie.Name = "stratum_session";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

                // Fenêtre de révocation différée (ADR-0017 §Négatif) : la garde endpoint lit des claims
                // "permission" figés au sign-in (projetés des rôles realm). Le cookie étant en expiration
                // glissante, il se ré-émet avec les mêmes claims SANS rejouer OnTokenValidated — une
                // révocation de rôle n'est donc PAS honorée immédiatement (fenêtre ≥ 8 h pour une session
                // active sur les permissions NON sensibles). Pour les permissions SENSIBLES
                // (liakont.actions/liakont.settings), RDF10 BORNE cette fenêtre : OnTokenValidated pose un
                // cap de durée absolu court (sans glissement) sur ces sessions (voir
                // SensitivePermissionRevocation + KeycloakSettings.SensitivePermissionRevocationWindow).
                // Ce défaut glissant 8 h reste celui des sessions non sensibles (lecture, supervision).
                options.SlidingExpiration = true;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);

                // When cookie challenge is triggered, redirect to OIDC or local login
                options.Events.OnRedirectToLogin = ctx =>
                {
                    // API requests get 401 instead of redirect
                    if (ctx.Request.Path.StartsWithSegments("/api"))
                    {
                        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    }

                    // When Keycloak login is active, challenge via OIDC
                    if (kc.IsKeycloakLoginActive)
                    {
                        return ctx.HttpContext.ChallengeAsync(
                            OpenIdConnectDefaults.AuthenticationScheme,
                            new AuthenticationProperties { RedirectUri = ctx.Properties.RedirectUri ?? "/" });
                    }

                    // Fallback: redirect to local login page (UseKeycloak=false)
                    ctx.Response.Redirect(ctx.RedirectUri);
                    return Task.CompletedTask;
                };
            });
    }

    /// <summary>
    /// Configures common OIDC options shared across all realm handlers.
    /// </summary>
    private static void ConfigureOidcOptions(
        OpenIdConnectOptions options,
        string authority,
        string clientId,
        string clientSecret,
        bool requireHttpsMetadata,
        TimeSpan sensitivePermissionRevocationWindow,
        string postLogoutRedirectUri = "/login",
        string metadataAddress = "")
    {
        options.Authority = authority;

        // Adresse de découverte EXPLICITE (appliance Docker derrière un reverse proxy, F12 §6.2) :
        // permet de pointer le back-channel sur l'IdP en réseau INTERNE pendant que l'Authority
        // publique (issuer + endpoint d'autorisation) reste celle exposée par Keycloak. Vide = la
        // découverte est dérivée de l'Authority (comportement par défaut, dev/test inchangés).
        if (!string.IsNullOrWhiteSpace(metadataAddress))
        {
            options.MetadataAddress = metadataAddress;
        }

        options.ClientId = clientId;
        options.ClientSecret = clientSecret;
        options.RequireHttpsMetadata = requireHttpsMetadata;
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = true;

        // Disable Pushed Authorization Requests — Keycloak dev rejects the redirect_uri via PAR
        options.PushedAuthorizationBehavior = Microsoft.AspNetCore.Authentication.OpenIdConnect.PushedAuthorizationBehavior.Disable;

        // Request standard scopes
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");

        // Sign-in to cookie scheme after OIDC callback
        options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;

        // Map OIDC standard claims to the names HttpActorContextAccessor expects
        options.ClaimActions.MapJsonKey("display_name", "name");
        options.ClaimActions.MapJsonKey("company_id", "company_id");
        options.ClaimActions.MapJsonKey("zoneinfo", "zoneinfo");
        options.ClaimActions.MapJsonKey("locale", "locale");
        options.ClaimActions.MapJsonKey("stratum_user_id", "stratum_user_id");

        // Map sub → NameIdentifier so HttpActorContextAccessor reads UserId
        options.ClaimActions.MapUniqueJsonKey(
            System.Security.Claims.ClaimTypes.NameIdentifier, "sub");

        // Conserve le claim « iss » dans le cookie (le handler OIDC le SUPPRIME par défaut) :
        // OidcIssuerTenantResolver peut alors résoudre le tenant du circuit Blazor même sans
        // sous-domaine (bug-inbox « amorçage console », facette résolution de tenant circuit).
        options.ClaimActions.Remove("iss");

        // Post-logout redirect: Keycloak sends the user here after end_session
        options.SignedOutRedirectUri = postLogoutRedirectUri;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            NameClaimType = "preferred_username",
            RoleClaimType = "roles",
            ValidateIssuer = true,
        };

        // Auto-provision or update local User on each OIDC login
        options.Events = new Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectEvents
        {
            OnTokenValidated = async ctx =>
            {
                if (ctx.Principal is null)
                {
                    return;
                }

                // Tenant SUSPENDU (OPS03.4 lot B) : refus au SIGN-IN, AVANT le sync utilisateur et la
                // pose du cookie — l'utilisateur arrive sur une page explicite, pas sur une session
                // aussitôt coupée. La DÉCISION (issuer → realm → tenant → statut, super-admin jamais
                // bloqué) vit dans TenantSuspensionSignInGuard (testable sans OIDC) ; seul le POINT
                // d'accrochage est Keycloak (D10). Les sessions déjà ouvertes et l'API Bearer sont
                // couvertes par TenantSuspensionMiddleware.
                var shouldRefuseSignIn = await TenantSuspensionSignInGuard.ShouldRefuseAsync(
                    ctx.Principal,
                    ctx.HttpContext.RequestServices.GetRequiredService<IRealmRegistry>(),
                    ctx.HttpContext.RequestServices.GetRequiredService<ITenantSuspensionLookup>(),
                    ctx.HttpContext.RequestAborted);
                if (shouldRefuseSignIn)
                {
                    ctx.Response.Redirect("/tenant-suspendu");
                    ctx.HandleResponse();
                    return;
                }

                var syncService = ctx.HttpContext.RequestServices.GetRequiredService<Stratum.Modules.Identity.Application.IUserSyncService>();
                var userId = await syncService.SyncFromOidcClaimsAsync(ctx.Principal, ctx.HttpContext.RequestAborted);

                // Add stratum_user_id claim so HttpActorContextAccessor can read it, puis projette les
                // rôles realm → permissions (matrice §3) en claims "permission" sur le principal (ADR-0017).
                // Ce claim est le transport consommé par l'UI (ClaimsPermissionService) ET les endpoints
                // (PermissionAuthorizationHandler) : un seul mécanisme d'autorisation, sans hit base par
                // requête. Catalogue IdP-agnostique (D10) : aucun appel Keycloak-spécifique ici.
                if (ctx.Principal.Identity is System.Security.Claims.ClaimsIdentity identity)
                {
                    identity.AddClaim(
                        new System.Security.Claims.Claim("stratum_user_id", userId.ToString("D")));

                    RolePermissionCatalog.ProjectPermissionClaims(identity);
                }

                // Borne la fenêtre de révocation des permissions SENSIBLES (RDF10, ADR-0017 §Négatif) :
                // une session non super-admin porteuse de liakont.actions/liakont.settings reçoit un cap
                // de durée ABSOLU court (sans glissement). À l'échéance, le cookie est rejeté et l'OIDC
                // ré-authentifie (transparent tant que la session SSO Keycloak vit), ce qui rejoue la
                // projection sur les rôles COURANTS — une révocation de rôle est alors honorée en ≤ fenêtre
                // au lieu des ≥ 8 h du cookie glissant. Les sessions non sensibles gardent le défaut glissant.
                var revocation = SensitivePermissionRevocation.Resolve(
                    ctx.Principal,
                    TimeProvider.System.GetUtcNow(),
                    sensitivePermissionRevocationWindow);
                if (revocation.Cap && ctx.Properties is not null)
                {
                    ctx.Properties.ExpiresUtc = revocation.ExpiresUtc;
                    ctx.Properties.AllowRefresh = false;
                }

                // Strip bulky tokens from auth properties to keep the session cookie small.
                // Only the id_token is retained (needed for logout id_token_hint).
                // Without this, the cookie exceeds ~4 KB, gets chunked, and stale chunks
                // from a previous session can cause re-login failures.
                if (ctx.Properties is not null)
                {
                    ctx.Properties.Items.Remove(".Token.access_token");
                    ctx.Properties.Items.Remove(".Token.refresh_token");
                    ctx.Properties.Items.Remove(".Token.token_type");
                    ctx.Properties.Items.Remove(".Token.expires_at");
                }
            },
        };
    }
}
