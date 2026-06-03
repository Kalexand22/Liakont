namespace Stratum.Common.Abstractions.Actions;

/// <summary>
/// Decorates a method on an <see cref="IActionHook"/> implementation to bind it
/// to a specific logical action name and pipeline stage.
/// </summary>
/// <remarks>
/// The decorated method must have the signature:
/// <code>Task&lt;ActionResult&gt; MethodName(ActionContext&lt;TEntity&gt; context)</code>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class HookAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HookAttribute"/> class.
    /// </summary>
    /// <param name="actionName">
    /// Logical dot-separated action name (e.g., "sale.sale-order.confirmed").
    /// </param>
    /// <param name="stage">The pipeline stage at which this hook executes.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="actionName"/> is null or empty.
    /// </exception>
    public HookAttribute(string actionName, ActionStage stage)
    {
        if (string.IsNullOrWhiteSpace(actionName))
        {
            throw new ArgumentException("Action name must not be null or empty.", nameof(actionName));
        }

        ActionName = actionName;
        Stage = stage;
    }

    /// <summary>
    /// Logical dot-separated action name this hook reacts to.
    /// </summary>
    public string ActionName { get; }

    /// <summary>
    /// The pipeline stage at which this hook executes.
    /// </summary>
    public ActionStage Stage { get; }
}
