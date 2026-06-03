namespace Liakont.Host.Behaviors;

using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Security;

internal sealed partial class ActorContextBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IActorContextAccessor _accessor;

    private readonly ILogger<ActorContextBehavior<TRequest, TResponse>> _logger;

    public ActorContextBehavior(
        IActorContextAccessor accessor,
        ILogger<ActorContextBehavior<TRequest, TResponse>> logger)
    {
        _accessor = accessor;
        _logger = logger;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var actor = _accessor.Current;
        var requestType = typeof(TRequest).Name;

        using (_logger.BeginScope(new Dictionary<string, object?>
        {
            ["UserId"] = actor.UserId,
            ["CorrelationId"] = actor.CorrelationId,
            ["IsAuthenticated"] = actor.IsAuthenticated,
            ["TenantId"] = actor.TenantId,
            ["RequestType"] = requestType,
        }))
        {
            LogRequestStart(_logger, requestType);
            var start = Stopwatch.GetTimestamp();

            try
            {
                var response = await next();
                var elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                LogRequestCompleted(_logger, requestType, elapsedMs);
                return response;
            }
            catch (Exception ex)
            {
                var elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                LogRequestFailed(_logger, requestType, elapsedMs, ex);
                throw;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Handling {RequestType}")]
    private static partial void LogRequestStart(ILogger logger, string requestType);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Handled {RequestType} in {ElapsedMs:F1}ms")]
    private static partial void LogRequestCompleted(ILogger logger, string requestType, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed {RequestType} after {ElapsedMs:F1}ms")]
    private static partial void LogRequestFailed(ILogger logger, string requestType, double elapsedMs, Exception exception);
}
