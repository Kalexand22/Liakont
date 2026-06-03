namespace Stratum.Common.Abstractions.Actions;

/// <summary>
/// Declares a named action chain: a composable sequence of validation, execution,
/// and notification steps for a specific entity type.
/// </summary>
/// <typeparam name="TEntity">The entity type this chain operates on.</typeparam>
public interface IActionChain<TEntity>
{
    /// <summary>
    /// Unique logical name for this chain (e.g., "sale.sale-order.confirm").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Configures the chain steps using the fluent builder API.
    /// </summary>
    void Configure(IActionChainBuilder<TEntity> builder);
}
