namespace Stratum.Common.Abstractions.Actions;

/// <summary>
/// Orchestrates the execution of <see cref="IActionStep{TEntity}"/> instances
/// in stage/order sequence. Stops on first error-severity finding.
/// </summary>
public interface IActionPipeline
{
    Task<ActionResult> ExecuteAsync<TEntity>(ActionContext<TEntity> context);
}
