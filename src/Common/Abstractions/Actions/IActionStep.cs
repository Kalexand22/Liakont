namespace Stratum.Common.Abstractions.Actions;

/// <summary>
/// A single step in the action pipeline.
/// Steps are sorted by <see cref="Stage"/> then by <see cref="Order"/> within the same stage.
/// </summary>
public interface IActionStep<TEntity>
{
    ActionStage Stage { get; }

    /// <summary>
    /// Execution order within the stage. Lower values execute first.
    /// </summary>
    int Order => 0;

    Task<ActionResult> ExecuteAsync(ActionContext<TEntity> context);
}
