namespace Liakont.Host.Behaviors;

using System.Reflection;
using MediatR;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Actions;
using Stratum.Common.Abstractions.Security;

/// <summary>
/// MediatR pipeline behavior that executes the <see cref="IActionPipeline"/>
/// before the handler for commands decorated with <see cref="UsesActionPipelineAttribute"/>.
/// <para>
/// - If the pipeline returns error findings → throws <see cref="ActionPipelineException"/> (handler never runs).
/// - If the pipeline returns warning/info findings → stores them in <see cref="IActionPipelineResult"/> and continues.
/// - If the command has no <c>[UsesActionPipeline]</c> attribute → passes through immediately.
/// </para>
/// Registered after <see cref="TenantPropagationBehavior{TRequest,TResponse}"/>
/// and before <see cref="EntityChangedBehavior{TRequest,TResponse}"/>.
/// </summary>
internal sealed partial class ActionPipelineBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    // Cached per closed generic type (one static per TRequest/TResponse combination).
    // Null means "this command does not use the action pipeline".
    // ConfigError is non-null when [UsesActionPipeline] is present but IActionPipelineCommand<T> is missing.
    private static readonly Func<object, IActionPipeline, IActorContext, CancellationToken, Task<ActionResult>>?
        PipelineInvoker;

    private static readonly string? ConfigError;

    private readonly IActionPipeline _pipeline;
    private readonly IActionPipelineResultWriter _resultHolder;
    private readonly IActorContextAccessor _actorAccessor;
    private readonly ILogger<ActionPipelineBehavior<TRequest, TResponse>> _logger;

    static ActionPipelineBehavior()
    {
        (PipelineInvoker, ConfigError) = BuildPipelineInvoker();
    }

    public ActionPipelineBehavior(
        IActionPipeline pipeline,
        IActionPipelineResultWriter resultHolder,
        IActorContextAccessor actorAccessor,
        ILogger<ActionPipelineBehavior<TRequest, TResponse>> logger)
    {
        _pipeline = pipeline;
        _resultHolder = resultHolder;
        _actorAccessor = actorAccessor;
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (ConfigError is not null)
        {
            throw new InvalidOperationException(ConfigError);
        }

        if (PipelineInvoker is null)
        {
            return await next();
        }

        var actor = _actorAccessor.Current;
        var requestType = typeof(TRequest).Name;

        LogPipelineStart(_logger, requestType);

        var result = await PipelineInvoker(request, _pipeline, actor, cancellationToken);
        _resultHolder.Result = result;

        if (!result.IsSuccess)
        {
            var errorFindings = result.Findings
                .Where(f => f.Severity == ActionFindingSeverity.Error)
                .ToList();

            LogPipelineBlocked(_logger, requestType, errorFindings.Count);
            throw new ActionPipelineException(errorFindings);
        }

        var warningCount = result.Findings.Count(f => f.Severity == ActionFindingSeverity.Warning);
        if (warningCount > 0)
        {
            LogPipelineWarnings(_logger, requestType, warningCount);
        }

        return await next();
    }

    /// <summary>
    /// Builds a cached delegate that bridges from the non-generic behavior to the
    /// generic <see cref="IActionPipeline.ExecuteAsync{TEntity}"/> call.
    /// Returns null if <typeparamref name="TRequest"/> does not have
    /// <see cref="UsesActionPipelineAttribute"/> or does not implement
    /// <see cref="IActionPipelineCommand{TEntity}"/>.
    /// </summary>
    private static (Func<object, IActionPipeline, IActorContext, CancellationToken, Task<ActionResult>>? Invoker, string? Error)
        BuildPipelineInvoker()
    {
        if (!typeof(TRequest).IsDefined(typeof(UsesActionPipelineAttribute), inherit: false))
        {
            return (null, null);
        }

        // Find IActionPipelineCommand<TEntity> on the request type.
        var commandInterface = Array.Find(
            typeof(TRequest).GetInterfaces(),
            i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IActionPipelineCommand<>));

        if (commandInterface is null)
        {
            return (null, $"{typeof(TRequest).Name} has [UsesActionPipeline] but does not implement " +
                          $"IActionPipelineCommand<TEntity>. Either add the interface or remove the attribute.");
        }

        var entityType = commandInterface.GetGenericArguments()[0];

        // Create a closed-generic delegate via InvokeTyped<TEntity>.
        var method = typeof(ActionPipelineBehavior<TRequest, TResponse>)
            .GetMethod(nameof(InvokeTyped), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(entityType);

        var delegateType = typeof(Func<object, IActionPipeline, IActorContext, CancellationToken, Task<ActionResult>>);
        var invoker = (Func<object, IActionPipeline, IActorContext, CancellationToken, Task<ActionResult>>)Delegate.CreateDelegate(
            delegateType, method);
        return (invoker, null);
    }

    private static Task<ActionResult> InvokeTyped<TEntity>(
        object request,
        IActionPipeline pipeline,
        IActorContext actor,
        CancellationToken cancellationToken)
    {
        var cmd = (IActionPipelineCommand<TEntity>)request;
        var context = new ActionContext<TEntity>
        {
            Entity = cmd.GetPipelineEntity(),
            Actor = actor,
            ChangedFields = cmd.GetChangedFields(),
            CancellationToken = cancellationToken,
        };
        return pipeline.ExecuteAsync(context);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Executing action pipeline for {RequestType}")]
    private static partial void LogPipelineStart(ILogger logger, string requestType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Action pipeline blocked {RequestType} with {ErrorCount} error(s)")]
    private static partial void LogPipelineBlocked(ILogger logger, string requestType, int errorCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Action pipeline passed {RequestType} with {WarningCount} warning(s)")]
    private static partial void LogPipelineWarnings(ILogger logger, string requestType, int warningCount);
}
