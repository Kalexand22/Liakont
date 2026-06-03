namespace Stratum.Common.Infrastructure.Actions.Hooks;

using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Actions;

/// <summary>
/// Discovers <see cref="IActionHook"/> implementations via DI at first use,
/// scans their methods for <see cref="HookAttribute"/>, and caches the index.
/// Executes matching hooks for a given (actionName, stage) pair.
/// </summary>
internal sealed partial class HookExecutor(
    IServiceProvider serviceProvider,
    ILogger<HookExecutor> logger) : IHookExecutor
{
    /// <summary>
    /// Static cache: maps (actionName, stage) → list of hook method descriptors.
    /// Built once per set of IActionHook types registered in DI.
    /// Keyed by sorted concatenation of type full names to avoid hash collisions.
    /// Thread-safe for concurrent reads across scoped instances.
    /// </summary>
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<(string ActionName, ActionStage Stage), IReadOnlyList<HookMethodDescriptor>>> IndexCache = new(StringComparer.Ordinal);

    public async Task<ActionResult> ExecuteHooksAsync(string actionName, ActionStage stage, object context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionName);
        ArgumentNullException.ThrowIfNull(context);

        var hooks = serviceProvider
            .GetService(typeof(IEnumerable<IActionHook>)) as IEnumerable<IActionHook>
            ?? [];

        var hookList = hooks.ToList();

        if (hookList.Count == 0)
        {
            return ActionResult.Success();
        }

        var index = GetOrBuildIndex(hookList);
        var key = (actionName, stage);

        if (!index.TryGetValue(key, out var descriptors) || descriptors.Count == 0)
        {
            return ActionResult.Success();
        }

        var allFindings = new List<ActionFinding>();

        foreach (var descriptor in descriptors)
        {
            var hook = hookList.FirstOrDefault(h => h.GetType() == descriptor.HookType);

            if (hook is null)
            {
                continue;
            }

            var result = await InvokeHookMethodAsync(hook, descriptor, context, stage);

            if (result is null)
            {
                continue;
            }

            if (result.Findings.Count > 0)
            {
                allFindings.AddRange(result.Findings);
            }

            // Per IHookExecutor contract: only Pre-Validation and Pre-Operation hooks can block
            if (!result.IsSuccess && stage is ActionStage.PreValidation or ActionStage.PreOperation)
            {
                return ActionResult.Failure(allFindings.AsReadOnly());
            }
        }

        return allFindings.Count > 0
            ? ActionResult.Success(allFindings.AsReadOnly())
            : ActionResult.Success();
    }

    /// <summary>
    /// Clears the static index cache. For testing only.
    /// </summary>
    internal static void ResetCache() => IndexCache.Clear();

    private static IReadOnlyDictionary<(string ActionName, ActionStage Stage), IReadOnlyList<HookMethodDescriptor>> GetOrBuildIndex(
        List<IActionHook> hooks)
    {
        var cacheKey = ComputeTypeSetKey(hooks);
        return IndexCache.GetOrAdd(cacheKey, _ => BuildIndex(hooks));
    }

    private static string ComputeTypeSetKey(List<IActionHook> hooks)
    {
        var names = hooks
            .Select(h => h.GetType().FullName ?? h.GetType().Name)
            .Distinct()
            .OrderBy(n => n, StringComparer.Ordinal);

        return string.Join("|", names);
    }

    private static Dictionary<(string ActionName, ActionStage Stage), IReadOnlyList<HookMethodDescriptor>> BuildIndex(
        List<IActionHook> hooks)
    {
        var map = new Dictionary<(string, ActionStage), List<HookMethodDescriptor>>();

        foreach (var hook in hooks)
        {
            var hookType = hook.GetType();
            var methods = hookType.GetMethods(BindingFlags.Public | BindingFlags.Instance);

            foreach (var method in methods)
            {
                var attr = method.GetCustomAttribute<HookAttribute>();

                if (attr is null)
                {
                    continue;
                }

                // Validate method signature: must return Task<ActionResult> and accept a single parameter
                if (method.ReturnType != typeof(Task<ActionResult>))
                {
                    continue;
                }

                var parameters = method.GetParameters();

                if (parameters.Length != 1)
                {
                    continue;
                }

                var key = (attr.ActionName, attr.Stage);

                if (!map.TryGetValue(key, out var list))
                {
                    list = [];
                    map[key] = list;
                }

                list.Add(new HookMethodDescriptor(hookType, method, parameters[0].ParameterType));
            }
        }

        return map.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<HookMethodDescriptor>)kvp.Value.AsReadOnly());
    }

    private async Task<ActionResult?> InvokeHookMethodAsync(
        IActionHook hook,
        HookMethodDescriptor descriptor,
        object context,
        ActionStage stage)
    {
        // Verify the context type is compatible with the method's parameter type
        if (!descriptor.ParameterType.IsInstanceOfType(context))
        {
            LogContextTypeMismatch(
                descriptor.HookType.Name,
                descriptor.Method.Name,
                descriptor.ParameterType.Name,
                context.GetType().Name);
            throw new ArgumentException(
                $"Hook method '{descriptor.HookType.Name}.{descriptor.Method.Name}' expects parameter " +
                $"of type '{descriptor.ParameterType.Name}' but received '{context.GetType().Name}'.");
        }

        try
        {
            var result = descriptor.Method.Invoke(hook, [context]);

            if (result is Task<ActionResult> task)
            {
                return await task;
            }

            LogInvalidReturnType(descriptor.HookType.Name, descriptor.Method.Name);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ArgumentException)
        {
            var inner = ex is TargetInvocationException tie ? tie.InnerException ?? ex : ex;

            if (inner is OperationCanceledException or ArgumentException)
            {
                ExceptionDispatchInfo.Capture(inner).Throw();
            }

            LogHookException(inner, descriptor.HookType.Name, descriptor.Method.Name);

            // For blocking stages, a crashing hook must fail the action — silently skipping
            // would bypass a guard that was supposed to block.
            if (stage is ActionStage.PreValidation or ActionStage.PreOperation)
            {
                return ActionResult.Failure("hook", $"Hook '{descriptor.HookType.Name}.{descriptor.Method.Name}' threw an exception: {inner.Message}");
            }

            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Hook method '{HookType}.{Method}' expects '{ExpectedType}' but received '{ActualType}'.")]
    private partial void LogContextTypeMismatch(string hookType, string method, string expectedType, string actualType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Hook method '{HookType}.{Method}' did not return Task<ActionResult>. Skipping.")]
    private partial void LogInvalidReturnType(string hookType, string method);

    [LoggerMessage(Level = LogLevel.Error, Message = "Hook method '{HookType}.{Method}' threw an exception.")]
    private partial void LogHookException(Exception ex, string hookType, string method);

    /// <summary>
    /// Describes a hook method decorated with <see cref="HookAttribute"/>.
    /// </summary>
    internal sealed record HookMethodDescriptor(Type HookType, MethodInfo Method, Type ParameterType);
}
