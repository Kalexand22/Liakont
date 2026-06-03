namespace Liakont.Host.Security;

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Common.Abstractions.Security;

/// <summary>
/// Builds <see cref="IActorContext"/> from the current HTTP request.
/// Registered as Scoped — one instance per request.
/// <para>
/// <b>Ordering constraint:</b> <see cref="Current"/> must not be accessed before
/// <c>TenantMiddleware</c> has executed. The middleware pipeline enforces this:
/// <c>UseAuthentication → UseStratumMultiTenancy → UseAuthorization</c>.
/// MediatR handlers (the primary consumers) always run after middleware.
/// </para>
/// </summary>
internal sealed class HttpActorContextAccessor : IActorContextAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ITenantContext _tenantContext;

    private IActorContext? _cached;

    public HttpActorContextAccessor(IHttpContextAccessor httpContextAccessor, ITenantContext tenantContext)
    {
        _httpContextAccessor = httpContextAccessor;
        _tenantContext = tenantContext;
    }

    public IActorContext Current => _cached ??= Build(_httpContextAccessor.HttpContext, _tenantContext);

    private static ActorContext Build(HttpContext? ctx, ITenantContext tenantContext)
    {
        var correlationId = GetCorrelationId(ctx);

        if (ctx?.User?.Identity?.IsAuthenticated != true)
        {
            return new ActorContext
            {
                UserId = Guid.Empty,
                CorrelationId = correlationId,
                IsAuthenticated = false,
                TenantId = tenantContext.TenantId,
            };
        }

        // Resolve UserId: prefer stratum_user_id (set by UserSyncService or Keycloak attribute),
        // then fall back to NameIdentifier (which is sub for OIDC or User.Id for legacy JWT).
        var stratumUserId = ctx.User.FindFirstValue("stratum_user_id");
        var nameIdentifier = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? ctx.User.FindFirstValue("sub");
        var userIdRaw = stratumUserId ?? nameIdentifier;

        var userId = Guid.TryParse(userIdRaw, out var id)
            ? id
            : Guid.Empty;

        var companyId = Guid.TryParse(ctx.User.FindFirstValue("company_id"), out var cid)
            ? cid
            : (Guid?)null;

        // DisplayName: prefer display_name (mapped from OIDC "name" via ClaimActions),
        // fall back to OIDC standard "name" claim (for JwtBearer API path).
        var displayName = ctx.User.FindFirstValue("display_name")
            ?? ctx.User.FindFirstValue("name");

        // Email: standard claim, also check OIDC "email" for JwtBearer path.
        var email = ctx.User.FindFirstValue(ClaimTypes.Email)
            ?? ctx.User.FindFirstValue("email");

        return new ActorContext
        {
            UserId = userId,
            CorrelationId = correlationId,
            IsAuthenticated = true,
            DisplayName = displayName,
            Email = email,
            CompanyId = companyId,
            Timezone = ctx.User.FindFirstValue("zoneinfo"),
            Language = ctx.User.FindFirstValue("locale"),
            TenantId = tenantContext.TenantId,
        };
    }

    private static Guid GetCorrelationId(HttpContext? ctx)
    {
        if (ctx is null)
        {
            return Guid.NewGuid();
        }

        var header = ctx.Request.Headers["X-Correlation-Id"].FirstOrDefault();
        return Guid.TryParse(header, out var id) ? id : Guid.NewGuid();
    }
}
