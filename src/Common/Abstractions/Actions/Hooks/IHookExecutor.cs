namespace Stratum.Common.Abstractions.Actions;

/// <summary>
/// Discovers and executes cross-module hooks for a given action name and stage.
/// Hooks are discovered at startup via DI assembly scanning of <see cref="IActionHook"/>
/// implementations and their <see cref="HookAttribute"/>-decorated methods.
/// </summary>
public interface IHookExecutor
{
    /// <summary>
    /// Executes all hooks registered for the specified action name and stage.
    /// </summary>
    /// <param name="actionName">Logical dot-separated action name (e.g., "sale.sale-order.confirmed").</param>
    /// <param name="stage">The pipeline stage to execute hooks for.</param>
    /// <param name="context">
    /// The action context. Typed as <see langword="object"/> to support cross-module scenarios
    /// where the hook consumer may not reference the originating module's entity type.
    /// Implementations must validate that the object is an <c>ActionContext&lt;TEntity&gt;</c>
    /// and throw <see cref="ArgumentException"/> if the type does not match.
    /// </param>
    /// <returns>
    /// Aggregated result from all hooks. If any Pre-Validation or Pre-Operation hook returns
    /// an error-severity finding, the result is a failure.
    /// </returns>
    Task<ActionResult> ExecuteHooksAsync(string actionName, ActionStage stage, object context);
}
