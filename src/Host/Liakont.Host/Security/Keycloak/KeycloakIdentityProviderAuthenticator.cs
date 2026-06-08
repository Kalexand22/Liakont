namespace Liakont.Host.Security.Keycloak;

using System.IdentityModel.Tokens.Jwt;
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
            ConfigureOidcOptions(options, kc.Authority, kc.ClientId, kc.ClientSecret, kc.RequireHttpsMetadata, kc.PostLogoutRedirectUri);
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
            })
            .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
            {
                options.LoginPath = "/login";
                options.Cookie.Name = "stratum_session";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
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
        string postLogoutRedirectUri = "/login")
    {
        options.Authority = authority;
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
