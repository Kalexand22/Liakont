namespace Stratum.Common.Infrastructure.UiRules;

using System.Collections.Concurrent;
using System.Linq.Expressions;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.UiRules;

/// <summary>
/// Evaluates <see cref="UiRule{T}"/> expressions against a DTO instance and
/// produces a <see cref="UiAttributeSet"/>. Compiled delegates are cached in a
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> for thread-safe reuse.
/// </summary>
internal sealed partial class UiRuleEngine(ILogger<UiRuleEngine> logger) : IUiRuleEngine
{
    /// <summary>
    /// Cache keyed by the expression object's identity. Since rule providers return the
    /// same <see cref="Expression"/> instances across calls, this ensures each lambda is
    /// compiled at most once.
    /// </summary>
    private static readonly ConcurrentDictionary<Expression, Delegate> CompiledCache = new(ReferenceEqualityComparer.Instance);

    public UiAttributeSet Evaluate<T>(T dto, IEnumerable<UiRule<T>> rules)
    {
        ArgumentNullException.ThrowIfNull(dto);
        ArgumentNullException.ThrowIfNull(rules);

        var attributes = new Dictionary<string, UiFieldAttributes>();

        foreach (var rule in rules)
        {
            try
            {
                var fieldName = ExtractFieldName(rule.FieldExpression);
                var fieldAttrs = EvaluateRule(dto, rule);

                if (attributes.TryGetValue(fieldName, out var existing))
                {
                    attributes[fieldName] = UiFieldAttributes.Merge(existing, fieldAttrs);
                }
                else
                {
                    attributes[fieldName] = fieldAttrs;
                }
            }
            catch (Exception ex)
            {
                LogRuleEvaluationFailed(ex, rule.FieldExpression.ToString());
            }
        }

        return new UiAttributeSet(attributes);
    }

    /// <summary>
    /// Extracts the field name from a member-access expression such as
    /// <c>x => x.Discount</c> or <c>x => (object)x.Discount</c> (boxing).
    /// </summary>
    internal static string ExtractFieldName<T>(Expression<Func<T, object>> fieldExpression)
    {
        var body = fieldExpression.Body;

        // Unwrap Convert / ConvertChecked nodes added by boxing value types.
        if (body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
        {
            body = unary.Operand;
        }

        if (body is MemberExpression member)
        {
            return member.Member.Name;
        }

        throw new ArgumentException(
            $"Cannot extract field name from expression '{fieldExpression}'. Expected a simple member access (e.g., x => x.Field).",
            nameof(fieldExpression));
    }

    private static UiFieldAttributes EvaluateRule<T>(T dto, UiRule<T> rule) =>
        new()
        {
            Hidden = EvaluatePredicate(dto, rule.HiddenWhen),
            ReadOnly = EvaluatePredicate(dto, rule.ReadOnlyWhen),
            Required = EvaluatePredicate(dto, rule.RequiredWhen),
            DomainFilter = EvaluateDomainFilter(dto, rule.DomainFilter),
        };

    private static bool EvaluatePredicate<T>(T dto, Expression<Func<T, bool>>? expression)
    {
        if (expression is null)
        {
            return false;
        }

        var compiled = CompileAndCache(expression);
        return compiled(dto);
    }

    private static string? EvaluateDomainFilter<T>(T dto, Expression<Func<T, string>>? expression)
    {
        if (expression is null)
        {
            return null;
        }

        var compiled = CompileAndCache(expression);
        return compiled(dto);
    }

    private static Func<T, TResult> CompileAndCache<T, TResult>(Expression<Func<T, TResult>> expression) =>
        (Func<T, TResult>)CompiledCache.GetOrAdd(expression, static expr => ((LambdaExpression)expr).Compile());

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to evaluate UI rule for expression '{Expression}'. Skipping.")]
    private partial void LogRuleEvaluationFailed(Exception ex, string expression);
}
