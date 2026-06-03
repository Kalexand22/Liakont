namespace Stratum.Common.Abstractions.Actions;

/// <summary>
/// Marker interface for cross-module action hooks.
/// Hook methods are decorated with <see cref="HookAttribute"/> to declare which action
/// and stage they react to.
/// <para>
/// <b>Design rule (D4):</b> Pre-Validation and Pre-Operation hooks are read-only. They
/// can validate or enrich the response but must not write to another module's data.
/// Cross-module writes happen via IntegrationEvents in Post-Operation.
/// </para>
/// </summary>
/// <remarks>
/// Hook methods must have the signature:
/// <code>Task&lt;ActionResult&gt; MethodName(ActionContext&lt;TEntity&gt; context)</code>
/// </remarks>
public interface IActionHook;
