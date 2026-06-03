namespace Liakont.Host.MultiTenancy;

using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

/// <summary>
/// Propagates the tenant resolved by <see cref="TenantMiddleware"/> into the Blazor circuit's
/// DI scope. Without this, the circuit scope gets a fresh <see cref="MutableTenantContext"/>
/// that is never set — causing all database queries to hit the system DB instead of the tenant DB.
/// </summary>
/// <remarks>
/// <para>
/// In Blazor Server, each SignalR circuit creates its own <c>IServiceScope</c>, separate from
/// the HTTP request scope where the middleware ran. The middleware stores the resolved tenant ID
/// in <see cref="HttpContext.Items"/> (via <see cref="TenantMiddleware.TenantHttpContextKey"/>),
/// and this handler copies it into the circuit's scoped <see cref="MutableTenantContext"/>
/// when the circuit opens.
/// </para>
/// <para>
/// <see cref="IHttpContextAccessor.HttpContext"/> is available during
/// <see cref="OnCircuitOpenedAsync"/> because the circuit is being opened from the WebSocket
/// upgrade HTTP request.
/// </para>
/// </remarks>
internal sealed partial class TenantCircuitHandler : CircuitHandler
{
    private readonly MutableTenantContext _tenantContext;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<TenantCircuitHandler> _logger;

    public TenantCircuitHandler(
        MutableTenantContext tenantContext,
        IHttpContextAccessor httpContextAccessor,
        ILogger<TenantCircuitHandler> logger)
    {
        _tenantContext = tenantContext;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return Task.CompletedTask;
        }

        if (httpContext.Items.TryGetValue(TenantMiddleware.TenantHttpContextKey, out var value)
            && value is string tenantId)
        {
            _tenantContext.TenantId = tenantId;
            LogTenantPropagated(_logger, tenantId);
        }
        else
        {
            LogNoTenantInHttpContext(_logger);
        }

        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Tenant '{TenantId}' propagated to Blazor circuit scope")]
    private static partial void LogTenantPropagated(ILogger logger, string tenantId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "No tenant found in HttpContext.Items — circuit will use system database")]
    private static partial void LogNoTenantInHttpContext(ILogger logger);
}
