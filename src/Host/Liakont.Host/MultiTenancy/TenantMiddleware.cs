namespace Liakont.Host.MultiTenancy;

using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Resolves the current tenant on every HTTP request using the composite resolver chain
/// (subdomain > header > JWT claim) and stores the result in <see cref="ITenantContext"/>.
/// </summary>
/// <remarks>
/// Must run after <c>UseAuthentication()</c> so that JWT claims are available.
/// Returns 400 Bad Request for API endpoints that require a tenant but none is resolved.
/// Non-API requests (Blazor pages, static files) are allowed through without a tenant —
/// the UI handles tenant context via its own mechanisms.
/// </remarks>
internal sealed partial class TenantMiddleware
{
    /// <summary>
    /// Key used to store the resolved tenant ID in <see cref="HttpContext.Items"/>
    /// so that <see cref="TenantCircuitHandler"/> can propagate it to the Blazor circuit scope.
    /// </summary>
    internal const string TenantHttpContextKey = "Stratum.TenantId";

    /// <summary>
    /// Tenant ID must be alphanumeric with hyphens, 1–63 chars, starting with a letter or digit.
    /// Prevents injection via header or JWT claim values.
    /// </summary>
    private static readonly Regex ValidTenantIdPattern = ValidTenantIdRegex();

    private readonly RequestDelegate _next;
    private readonly ILogger<TenantMiddleware> _logger;
    private readonly bool _enforceOnApi;

    public TenantMiddleware(
        RequestDelegate next,
        ILogger<TenantMiddleware> logger,
        IOptions<MultiTenancyOptions> options)
    {
        _next = next;
        _logger = logger;
        _enforceOnApi = options.Value.EnforceOnApiEndpoints;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var compositeResolver = context.RequestServices.GetRequiredService<CompositeTenantResolver>();
        var tenantContext = context.RequestServices.GetRequiredService<MutableTenantContext>();

        var tenantId = compositeResolver.Resolve();

        // Validate format to prevent injection (schema names, SQL, etc.)
        if (tenantId is not null && !ValidTenantIdPattern.IsMatch(tenantId))
        {
            LogInvalidTenantId(_logger, tenantId);
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://httpstatuses.io/400",
                title = "Invalid Tenant ID",
                status = 400,
                detail = "Tenant identifier must be 1-63 characters, alphanumeric with hyphens, starting with a letter or digit.",
            });
            return;
        }

        tenantContext.TenantId = tenantId;

        // Store in HttpContext.Items so that Blazor circuit handlers can propagate
        // the tenant to the circuit's own DI scope (which is separate from the HTTP scope).
        if (tenantId is not null)
        {
            context.Items[TenantHttpContextKey] = tenantId;
            LogTenantResolved(_logger, tenantId);
        }
        else if (_enforceOnApi && IsTenantRequiredEndpoint(context))
        {
            LogTenantRequired(_logger, context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://httpstatuses.io/400",
                title = "Tenant Required",
                status = 400,
                detail = "A tenant identifier is required for this endpoint. "
                       + "Provide it via subdomain, X-Tenant-Id header, or tenant_id JWT claim.",
            });
            return;
        }

        await _next(context);
    }

    /// <summary>
    /// Determines whether the current request targets an API endpoint that requires tenant context.
    /// API endpoints (starting with /api/) require a tenant unless they are explicitly tenant-free
    /// (e.g., /api/v1/admin/*, /api/v1/identity/login).
    /// </summary>
    private static bool IsTenantRequiredEndpoint(HttpContext context)
    {
        var path = context.Request.Path.Value;
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        // Portal endpoints are tenant-free (public access, no tenant context)
        if (path.StartsWith("/portal", StringComparison.OrdinalIgnoreCase)
            && (path.Length == "/portal".Length || path["/portal".Length] == '/'))
        {
            return false;
        }

        // Only API endpoints require tenant
        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Admin and identity endpoints are tenant-free
        if (MatchesExcludedSegment(path, "/admin")
            || MatchesExcludedSegment(path, "/identity"))
        {
            return false;
        }

        // Unauthenticated requests don't require tenant (they'll fail on auth first)
        return context.User.Identity?.IsAuthenticated == true;
    }

    /// <summary>
    /// Checks if the path contains the given segment as a path component (e.g., "/admin" or "/admin/...").
    /// </summary>
    private static bool MatchesExcludedSegment(string path, string segment)
    {
        var idx = path.IndexOf(segment, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return false;
        }

        var afterSegment = idx + segment.Length;
        return afterSegment >= path.Length || path[afterSegment] == '/';
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Tenant resolved: {TenantId}")]
    private static partial void LogTenantResolved(ILogger logger, string tenantId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Tenant required but not resolved for {Path}")]
    private static partial void LogTenantRequired(ILogger logger, PathString path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid tenant ID format rejected: {TenantId}")]
    private static partial void LogInvalidTenantId(ILogger logger, string tenantId);

    [GeneratedRegex(@"^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ValidTenantIdRegex();
}
