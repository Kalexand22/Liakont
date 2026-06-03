namespace Liakont.Host.Behaviors;

using MediatR;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Common.Abstractions.Security;

/// <summary>
/// MediatR pipeline behavior that verifies tenant consistency and provides
/// per-request tenant logging. Reads <see cref="ITenantContext"/> (set by TenantMiddleware)
/// and validates that the <see cref="IActorContext.TenantId"/> matches.
/// Throws <see cref="InvalidOperationException"/> on mismatch to prevent cross-tenant data leakage.
/// </summary>
internal sealed partial class TenantPropagationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ITenantContext _tenantContext;
    private readonly IActorContextAccessor _actorContextAccessor;
    private readonly ILogger<TenantPropagationBehavior<TRequest, TResponse>> _logger;

    public TenantPropagationBehavior(
        ITenantContext tenantContext,
        IActorContextAccessor actorContextAccessor,
        ILogger<TenantPropagationBehavior<TRequest, TResponse>> logger)
    {
        _tenantContext = tenantContext;
        _actorContextAccessor = actorContextAccessor;
        _logger = logger;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var actor = _actorContextAccessor.Current;
        var requestType = typeof(TRequest).Name;

        if (_tenantContext.IsResolved && tenantId is not null)
        {
            LogTenantResolved(_logger, tenantId, requestType);
        }
        else
        {
            LogNoTenant(_logger, requestType);
        }

        // Verify consistency: the actor context must have the same TenantId
        // as the tenant context (both are scoped per-request).
        // A mismatch indicates a bug (e.g., stale cached context) and could
        // lead to cross-tenant data leakage — fail fast.
        if (_tenantContext.IsResolved && actor.TenantId != tenantId)
        {
            throw new InvalidOperationException(
                $"Tenant mismatch for {requestType}: ActorContext has '{actor.TenantId}' but ITenantContext has '{tenantId}'. " +
                "This indicates a pipeline ordering or caching bug that could cause cross-tenant data leakage.");
        }

        return await next();
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Tenant {TenantId} propagated for {RequestType}")]
    private static partial void LogTenantResolved(ILogger logger, string tenantId, string requestType);

    [LoggerMessage(Level = LogLevel.Debug, Message = "No tenant resolved for {RequestType}")]
    private static partial void LogNoTenant(ILogger logger, string requestType);
}
