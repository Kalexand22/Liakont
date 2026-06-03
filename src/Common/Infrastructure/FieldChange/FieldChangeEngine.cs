namespace Stratum.Common.Infrastructure.FieldChange;

using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.FieldChange;
using Stratum.Common.Abstractions.Security;

/// <summary>
/// Discovers <see cref="IFieldChangeHandler{T}"/> implementations via DI, finds methods
/// decorated with <see cref="OnChangeAttribute"/>, and executes them for each changed field.
/// Supports cascade: when an onChange handler sets a field that itself has a handler,
/// the engine re-executes (up to <see cref="MaxCascadeIterations"/> iterations).
/// </summary>
internal sealed partial class FieldChangeEngine(
    IServiceProvider serviceProvider,
    ILogger<FieldChangeEngine> logger) : IFieldChangeEngine
{
    /// <summary>
    /// Maximum cascade iterations to prevent infinite loops when onChange handlers
    /// set fields that trigger other onChange handlers.
    /// </summary>
    internal const int MaxCascadeIterations = 10;

    /// <summary>
    /// Cache of handler method descriptors per entity type, keyed by field name.
    /// Thread-safe for concurrent reads across scoped instances sharing the same type cache.
    /// Assumption: handler DI registrations are immutable after startup (standard DI pattern).
    /// </summary>
    private static readonly ConcurrentDictionary<Type, IReadOnlyDictionary<string, IReadOnlyList<HandlerMethodDescriptor>>> MethodCache = new();

    public async Task<FieldChangeResult> ProcessChangesAsync<T>(
        T entity,
        IReadOnlySet<string> changedFields,
        IActorContext actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(changedFields);
        ArgumentNullException.ThrowIfNull(actor);

        if (changedFields.Count == 0)
        {
            return FieldChangeResult.Empty();
        }

        var handlers = serviceProvider
            .GetService(typeof(IEnumerable<IFieldChangeHandler<T>>)) as IEnumerable<IFieldChangeHandler<T>>
            ?? [];

        var handlerList = handlers.ToList();

        if (handlerList.Count == 0)
        {
            return FieldChangeResult.Empty();
        }

        var methodMap = GetOrBuildMethodMap<T>(handlerList);

        if (methodMap.Count == 0)
        {
            return FieldChangeResult.Empty();
        }

        var allResults = new List<FieldChangeResult>();
        var fieldsToProcess = new HashSet<string>(changedFields);
        var processedFields = new HashSet<string>();

        for (var iteration = 0; iteration < MaxCascadeIterations; iteration++)
        {
            ct.ThrowIfCancellationRequested();

            var iterationResults = new List<FieldChangeResult>();

            foreach (var fieldName in fieldsToProcess)
            {
                if (!methodMap.TryGetValue(fieldName, out var descriptors))
                {
                    continue;
                }

                foreach (var descriptor in descriptors)
                {
                    ct.ThrowIfCancellationRequested();

                    var context = new FieldChangeContext<T>
                    {
                        Entity = entity,
                        FieldName = fieldName,
                        Actor = actor,
                        CancellationToken = ct,
                    };

                    var handler = handlerList.FirstOrDefault(h => h.GetType() == descriptor.HandlerType);

                    if (handler is null)
                    {
                        continue;
                    }

                    var result = await InvokeHandlerMethodAsync(handler, descriptor, context);

                    if (result is not null)
                    {
                        iterationResults.Add(result);
                    }
                }
            }

            processedFields.UnionWith(fieldsToProcess);
            fieldsToProcess.Clear();

            if (iterationResults.Count == 0)
            {
                break;
            }

            allResults.AddRange(iterationResults);

            // Cascade: check if any newly set fields have their own handlers
            var merged = FieldChangeResult.Merge(iterationResults);

            foreach (var fieldName in merged.FieldsToSet.Keys)
            {
                if (!processedFields.Contains(fieldName) && methodMap.ContainsKey(fieldName))
                {
                    fieldsToProcess.Add(fieldName);
                }
            }

            if (fieldsToProcess.Count == 0)
            {
                break;
            }

            if (iteration == MaxCascadeIterations - 1)
            {
                LogMaxCascadeReached(typeof(T).Name, string.Join(", ", fieldsToProcess));
            }
        }

        return allResults.Count == 0
            ? FieldChangeResult.Empty()
            : FieldChangeResult.Merge(allResults);
    }

    /// <summary>
    /// Clears the static method cache. For testing only.
    /// </summary>
    internal static void ResetCache() => MethodCache.Clear();

    private static IReadOnlyDictionary<string, IReadOnlyList<HandlerMethodDescriptor>> GetOrBuildMethodMap<T>(
        List<IFieldChangeHandler<T>> handlers)
    {
        return MethodCache.GetOrAdd(typeof(T), _ => BuildMethodMap(handlers));
    }

    private static Dictionary<string, IReadOnlyList<HandlerMethodDescriptor>> BuildMethodMap<T>(
        List<IFieldChangeHandler<T>> handlers)
    {
        var expectedReturnType = typeof(Task<FieldChangeResult>);
        var expectedParamType = typeof(FieldChangeContext<T>);
        var map = new Dictionary<string, List<HandlerMethodDescriptor>>();

        foreach (var handler in handlers)
        {
            var handlerType = handler.GetType();
            var methods = handlerType.GetMethods(BindingFlags.Public | BindingFlags.Instance);

            foreach (var method in methods)
            {
                var attr = method.GetCustomAttribute<OnChangeAttribute>();

                if (attr is null)
                {
                    continue;
                }

                var parameters = method.GetParameters();

                if (method.ReturnType != expectedReturnType
                    || parameters.Length != 1
                    || !parameters[0].ParameterType.IsAssignableFrom(expectedParamType))
                {
                    // Invalid signature: skip at discovery time rather than failing every invocation.
                    continue;
                }

                if (!map.TryGetValue(attr.FieldName, out var list))
                {
                    list = [];
                    map[attr.FieldName] = list;
                }

                list.Add(new HandlerMethodDescriptor(handlerType, method));
            }
        }

        return map.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<HandlerMethodDescriptor>)kvp.Value.AsReadOnly());
    }

    private async Task<FieldChangeResult?> InvokeHandlerMethodAsync<T>(
        IFieldChangeHandler<T> handler,
        HandlerMethodDescriptor descriptor,
        FieldChangeContext<T> context)
    {
        try
        {
            var result = descriptor.Method.Invoke(handler, [context]);

            if (result is Task<FieldChangeResult> task)
            {
                return await task;
            }

            LogInvalidReturnType(descriptor.HandlerType.Name, descriptor.Method.Name);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // TargetInvocationException wraps sync throws; async faults rethrow directly.
            var inner = ex is TargetInvocationException tie ? tie.InnerException ?? ex : ex;
            LogHandlerException(inner, descriptor.HandlerType.Name, descriptor.Method.Name, context.FieldName);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "FieldChangeEngine reached max cascade iterations for entity type '{EntityType}'. Remaining fields not processed: {Fields}")]
    private partial void LogMaxCascadeReached(string entityType, string fields);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Handler method {HandlerType}.{Method} did not return Task<FieldChangeResult>. Skipping.")]
    private partial void LogInvalidReturnType(string handlerType, string method);

    [LoggerMessage(Level = LogLevel.Error, Message = "Handler method {HandlerType}.{Method} threw an exception for field '{Field}'.")]
    private partial void LogHandlerException(Exception ex, string handlerType, string method, string field);

    /// <summary>
    /// Describes a handler method decorated with <see cref="OnChangeAttribute"/>.
    /// </summary>
    internal sealed record HandlerMethodDescriptor(Type HandlerType, MethodInfo Method);
}
