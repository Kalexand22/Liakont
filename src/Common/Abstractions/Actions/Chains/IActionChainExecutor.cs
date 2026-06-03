namespace Stratum.Common.Abstractions.Actions;

/// <summary>
/// Discovers and executes named action chains.
/// Chains are resolved via DI from <see cref="IActionChain{TEntity}"/> registrations.
/// </summary>
public interface IActionChainExecutor
{
    /// <summary>
    /// Executes the chain identified by <paramref name="chainName"/> for the given context.
    /// Steps run in registration order with stop-on-error for validation and execution steps.
    /// </summary>
    /// <param name="chainName">Logical chain name (e.g., "sale.sale-order.confirm").</param>
    /// <param name="context">Action context carrying the entity, actor, and changed fields.</param>
    /// <returns>Aggregated result from all executed steps.</returns>
    Task<ActionResult> ExecuteChainAsync<TEntity>(string chainName, ActionContext<TEntity> context);
}
