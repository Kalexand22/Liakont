namespace Stratum.Common.Abstractions.Actions;

using Stratum.Common.Abstractions.Validation;

/// <summary>
/// Fluent builder for composing an action chain's step sequence.
/// Steps execute in registration order with stop-on-error semantics.
/// </summary>
/// <typeparam name="TEntity">The entity type the chain operates on.</typeparam>
public interface IActionChainBuilder<TEntity>
{
    /// <summary>
    /// Adds a validation step using the specified <see cref="IEntityValidator{T}"/>.
    /// Executed unconditionally.
    /// </summary>
    IActionChainBuilder<TEntity> Validate<TValidator>()
        where TValidator : IEntityValidator<TEntity>;

    /// <summary>
    /// Adds an execution step using the specified <see cref="IActionStep{TEntity}"/>.
    /// </summary>
    /// <param name="condition">Optional predicate. Step is skipped when this returns false.</param>
    IActionChainBuilder<TEntity> Execute<TStep>(Func<ActionContext<TEntity>, bool>? condition = null)
        where TStep : IActionStep<TEntity>;

    /// <summary>
    /// Adds a notification step using the specified <see cref="IActionStep{TEntity}"/>.
    /// Notifications run in Post-Operation and do not block the chain on failure.
    /// The <see cref="IActionStep{TEntity}.Stage"/> and <see cref="IActionStep{TEntity}.Order"/>
    /// properties are ignored for steps registered via this method.
    /// </summary>
    /// <param name="condition">Optional predicate. Step is skipped when this returns false.</param>
    IActionChainBuilder<TEntity> Notify<TStep>(Func<ActionContext<TEntity>, bool>? condition = null)
        where TStep : IActionStep<TEntity>;
}
