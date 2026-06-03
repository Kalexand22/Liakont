namespace Stratum.Common.Infrastructure.Actions.Chains;

using Stratum.Common.Abstractions.Actions;
using Stratum.Common.Abstractions.Validation;

/// <summary>
/// Concrete builder that collects chain step descriptors during
/// <see cref="IActionChain{TEntity}.Configure"/> and produces an ordered list.
/// </summary>
internal sealed class ActionChainBuilder<TEntity> : IActionChainBuilder<TEntity>
{
    private readonly List<ChainStepDescriptor> _steps = [];

    public IActionChainBuilder<TEntity> Validate<TValidator>()
        where TValidator : IEntityValidator<TEntity>
    {
        _steps.Add(new ChainStepDescriptor(
            ServiceType: typeof(TValidator),
            Kind: ChainStepKind.Validate,
            Condition: null));

        return this;
    }

    public IActionChainBuilder<TEntity> Execute<TStep>(Func<ActionContext<TEntity>, bool>? condition = null)
        where TStep : IActionStep<TEntity>
    {
        _steps.Add(new ChainStepDescriptor(
            ServiceType: typeof(TStep),
            Kind: ChainStepKind.Execute,
            Condition: condition));

        return this;
    }

    public IActionChainBuilder<TEntity> Notify<TStep>(Func<ActionContext<TEntity>, bool>? condition = null)
        where TStep : IActionStep<TEntity>
    {
        _steps.Add(new ChainStepDescriptor(
            ServiceType: typeof(TStep),
            Kind: ChainStepKind.Notify,
            Condition: condition));

        return this;
    }

    /// <summary>
    /// Returns the steps in registration order.
    /// </summary>
    internal IReadOnlyList<ChainStepDescriptor> Build() => _steps.AsReadOnly();
}
