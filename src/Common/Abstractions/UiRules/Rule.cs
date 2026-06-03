namespace Stratum.Common.Abstractions.UiRules;

using System.Linq.Expressions;

/// <summary>
/// Static entry point for the fluent UI-rule builder.
/// <para>
/// Usage:
/// <code>
/// Rule.For&lt;InvoiceDto&gt;(x =&gt; x.Discount)
///     .HiddenWhen(x =&gt; x.Status != "Draft")
///     .RequiredWhen(x =&gt; x.Status == "Confirmed");
/// </code>
/// </para>
/// </summary>
public static class Rule
{
    /// <summary>
    /// Starts building a <see cref="UiRule{T}"/> targeting the specified field.
    /// </summary>
    public static UiRuleBuilder<T> For<T>(Expression<Func<T, object>> fieldExpression)
    {
        ArgumentNullException.ThrowIfNull(fieldExpression);
        return new UiRuleBuilder<T>(fieldExpression);
    }
}
