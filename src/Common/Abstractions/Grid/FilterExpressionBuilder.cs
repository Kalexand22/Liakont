namespace Stratum.Common.Abstractions.Grid;

using System.Collections;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;

/// <summary>
/// Default implementation of <see cref="IFilterExpressionBuilder{TItem}"/>.
/// Builds LINQ expression trees from <see cref="FilterGroup"/> definitions,
/// resolving dot-notation property paths for related-table filtering.
/// </summary>
/// <typeparam name="TItem">The entity type being filtered.</typeparam>
public sealed class FilterExpressionBuilder<TItem> : IFilterExpressionBuilder<TItem>
{
    /// <inheritdoc />
    public Expression<Func<TItem, bool>> Build(FilterGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var parameter = Expression.Parameter(typeof(TItem), "x");
        var body = BuildGroup(group, parameter);
        return Expression.Lambda<Func<TItem, bool>>(body, parameter);
    }

    private static Expression BuildGroup(FilterGroup group, ParameterExpression parameter)
    {
        var expressions = new List<Expression>();

        foreach (var criterion in group.Criteria)
        {
            expressions.Add(BuildCriterion(criterion, parameter));
        }

        if (group.SubGroups is { Count: > 0 })
        {
            foreach (var subGroup in group.SubGroups)
            {
                expressions.Add(BuildGroup(subGroup, parameter));
            }
        }

        if (expressions.Count == 0)
        {
            return Expression.Constant(true);
        }

        return group.Logic == FilterLogic.And
            ? expressions.Aggregate(Expression.AndAlso)
            : expressions.Aggregate(Expression.OrElse);
    }

    private static Expression BuildCriterion(FilterCriterion criterion, ParameterExpression parameter)
    {
        ArgumentNullException.ThrowIfNull(criterion);

        if (string.IsNullOrWhiteSpace(criterion.Field))
        {
            throw new ArgumentException("Filter field must not be empty.", nameof(criterion));
        }

        var property = ResolvePropertyPath(parameter, criterion.Field);

        // DF-11 — A DateOnly value filtered against a DateTime/DateTimeOffset column
        // is expanded to full-day bounds so that "Equals today" means "any time on
        // today", "Before X" means "strictly before X 00:00", and so on. The start
        // and end are computed as UTC-midnight DateTimes; for user-timezone semantics
        // the caller normalizes to DateTime at the UI layer before handing the
        // criterion to the builder.
        var propertyTypeUnderlying = Nullable.GetUnderlyingType(property.Type) ?? property.Type;
        var isDateTimeProperty = propertyTypeUnderlying == typeof(DateTime) || propertyTypeUnderlying == typeof(DateTimeOffset);
        var hasDateOnlyValue = criterion.Value is DateOnly || criterion.ValueEnd is DateOnly;

        if (isDateTimeProperty && hasDateOnlyValue)
        {
            return BuildDateOnlyCriterion(property, propertyTypeUnderlying, criterion);
        }

        return criterion.Operator switch
        {
            FilterOperator.Equals => BuildEquals(property, criterion.Value),
            FilterOperator.NotEquals => Expression.Not(BuildEquals(property, criterion.Value)),
            FilterOperator.Contains => BuildStringMethod(property, criterion.Value, nameof(string.Contains)),
            FilterOperator.NotContains => BuildNegatedStringMethod(property, criterion.Value, nameof(string.Contains)),
            FilterOperator.StartsWith => BuildStringMethod(property, criterion.Value, nameof(string.StartsWith)),
            FilterOperator.EndsWith => BuildStringMethod(property, criterion.Value, nameof(string.EndsWith)),
            FilterOperator.GreaterThan => BuildComparison(property, criterion.Value, Expression.GreaterThan),
            FilterOperator.GreaterThanOrEqual => BuildComparison(property, criterion.Value, Expression.GreaterThanOrEqual),
            FilterOperator.LessThan => BuildComparison(property, criterion.Value, Expression.LessThan),
            FilterOperator.LessThanOrEqual => BuildComparison(property, criterion.Value, Expression.LessThanOrEqual),
            FilterOperator.Between => BuildBetween(property, criterion.Value, criterion.ValueEnd),
            FilterOperator.NotBetween => BuildNullGuardedNot(property, BuildBetween(property, criterion.Value, criterion.ValueEnd)),
            FilterOperator.In => BuildIn(property, criterion.Value),
            FilterOperator.NotIn => BuildNullGuardedNot(property, BuildIn(property, criterion.Value)),
            FilterOperator.Before => BuildComparison(property, criterion.Value, Expression.LessThan),
            FilterOperator.After => BuildComparison(property, criterion.Value, Expression.GreaterThan),
            FilterOperator.RelativePeriod => BuildRelativePeriod(property, criterion.Value),
            FilterOperator.IsNull => BuildIsNull(property),
            FilterOperator.IsNotNull => Expression.Not(BuildIsNull(property)),
            _ => throw new ArgumentOutOfRangeException(nameof(criterion), $"Unsupported operator: {criterion.Operator}"),
        };
    }

    /// <summary>
    /// Rewrites a date-only criterion into a DateTime/DateTimeOffset expression using
    /// full-day boundary expansion (DF-11). Start = 00:00:00 UTC, End = 23:59:59.9999999 UTC.
    /// Bypasses ConvertConstant so DateTime/DateTimeOffset values (which do not round-trip
    /// cleanly through Convert.ChangeType) go directly into Expression.Constant.
    /// </summary>
    private static Expression BuildDateOnlyCriterion(
        Expression property,
        Type propertyUnderlying,
        FilterCriterion criterion)
    {
        static DateTime StartOfDay(DateOnly d) => d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        static DateTime EndOfDay(DateOnly d) => d.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        DateOnly? start = criterion.Value as DateOnly?;
        DateOnly? end = criterion.ValueEnd as DateOnly?;

        ConstantExpression BoundStart(DateOnly d) => propertyUnderlying == typeof(DateTimeOffset)
            ? Expression.Constant(new DateTimeOffset(StartOfDay(d), TimeSpan.Zero), property.Type)
            : Expression.Constant(StartOfDay(d), property.Type);

        ConstantExpression BoundEnd(DateOnly d) => propertyUnderlying == typeof(DateTimeOffset)
            ? Expression.Constant(new DateTimeOffset(EndOfDay(d), TimeSpan.Zero), property.Type)
            : Expression.Constant(EndOfDay(d), property.Type);

        static BinaryExpression Range(Expression p, ConstantExpression s, ConstantExpression e) =>
            Expression.AndAlso(
                Expression.GreaterThanOrEqual(p, s),
                Expression.LessThanOrEqual(p, e));

        switch (criterion.Operator)
        {
            case FilterOperator.Equals:
                if (start is null)
                {
                    throw new ArgumentException("Equals operator with DateOnly value requires Value.");
                }

                return Range(property, BoundStart(start.Value), BoundEnd(start.Value));

            case FilterOperator.NotEquals:
                if (start is null)
                {
                    throw new ArgumentException("NotEquals operator with DateOnly value requires Value.");
                }

                return BuildNullGuardedNot(
                    property,
                    Range(property, BoundStart(start.Value), BoundEnd(start.Value)));

            case FilterOperator.Before:
            case FilterOperator.LessThan:
                if (start is null)
                {
                    throw new ArgumentException("Before/LessThan operator with DateOnly value requires Value.");
                }

                return Expression.LessThan(property, BoundStart(start.Value));

            case FilterOperator.LessThanOrEqual:
                if (start is null)
                {
                    throw new ArgumentException("LessThanOrEqual operator with DateOnly value requires Value.");
                }

                return Expression.LessThanOrEqual(property, BoundEnd(start.Value));

            case FilterOperator.After:
            case FilterOperator.GreaterThan:
                if (start is null)
                {
                    throw new ArgumentException("After/GreaterThan operator with DateOnly value requires Value.");
                }

                return Expression.GreaterThan(property, BoundEnd(start.Value));

            case FilterOperator.GreaterThanOrEqual:
                if (start is null)
                {
                    throw new ArgumentException("GreaterThanOrEqual operator with DateOnly value requires Value.");
                }

                return Expression.GreaterThanOrEqual(property, BoundStart(start.Value));

            case FilterOperator.Between:
                if (start is null || end is null)
                {
                    throw new ArgumentException("Between operator with DateOnly requires both Value and ValueEnd.");
                }

                return Range(property, BoundStart(start.Value), BoundEnd(end.Value));

            case FilterOperator.NotBetween:
                if (start is null || end is null)
                {
                    throw new ArgumentException("NotBetween operator with DateOnly requires both Value and ValueEnd.");
                }

                return BuildNullGuardedNot(
                    property,
                    Range(property, BoundStart(start.Value), BoundEnd(end.Value)));

            case FilterOperator.IsNull:
                return BuildIsNull(property);

            case FilterOperator.IsNotNull:
                return Expression.Not(BuildIsNull(property));

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(criterion),
                    $"Unsupported date-only operator: {criterion.Operator}");
        }
    }

    private static Expression ResolvePropertyPath(Expression root, string path)
    {
        var current = root;
        foreach (var segment in path.Split('.'))
        {
            var property = current.Type.GetProperty(
                segment,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (property is null)
            {
                throw new ArgumentException(
                    $"Property '{segment}' not found on type '{current.Type.Name}' " +
                    $"(full path: '{path}').");
            }

            current = Expression.Property(current, property);
        }

        return current;
    }

    private static BinaryExpression BuildEquals(Expression property, object? value)
    {
        var constant = ConvertConstant(value, property.Type);
        return Expression.Equal(property, constant);
    }

    private static BinaryExpression BuildStringMethod(Expression property, object? value, string methodName)
    {
        if (property.Type != typeof(string))
        {
            throw new InvalidOperationException(
                $"Operator '{methodName}' requires a string property, but got '{property.Type.Name}'.");
        }

        var method = typeof(string).GetMethod(methodName, new[] { typeof(string) })!;
        var constant = Expression.Constant(value?.ToString() ?? string.Empty, typeof(string));

        // Handle null: if property is null, return false
        var nullCheck = Expression.NotEqual(property, Expression.Constant(null, typeof(string)));
        var call = Expression.Call(property, method, constant);
        return Expression.AndAlso(nullCheck, call);
    }

    /// <summary>
    /// Negated string method: null property returns false (not true via De Morgan).
    /// </summary>
    private static BinaryExpression BuildNegatedStringMethod(Expression property, object? value, string methodName)
    {
        if (property.Type != typeof(string))
        {
            throw new InvalidOperationException(
                $"Operator 'Not{methodName}' requires a string property, but got '{property.Type.Name}'.");
        }

        var method = typeof(string).GetMethod(methodName, new[] { typeof(string) })!;
        var constant = Expression.Constant(value?.ToString() ?? string.Empty, typeof(string));

        // null property → false (exclude nulls), non-null property → !method()
        var nullCheck = Expression.NotEqual(property, Expression.Constant(null, typeof(string)));
        var call = Expression.Not(Expression.Call(property, method, constant));
        return Expression.AndAlso(nullCheck, call);
    }

    private static Expression BuildNullGuardedNot(Expression property, Expression inner)
    {
        // For nullable types, null → false (match SQL NOT behavior where NULL NOT IN/NOT BETWEEN = NULL → false)
        if (property.Type == typeof(string) || Nullable.GetUnderlyingType(property.Type) is not null)
        {
            var nullCheck = Expression.NotEqual(property, Expression.Constant(null, property.Type));
            return Expression.AndAlso(nullCheck, Expression.Not(inner));
        }

        // Non-nullable types can never be null
        return Expression.Not(inner);
    }

    private static BinaryExpression BuildRelativePeriod(Expression property, object? value)
    {
        if (value is not RelativeDatePeriod period)
        {
            if (value is string s && Enum.TryParse<RelativeDatePeriod>(s, ignoreCase: true, out var parsed))
            {
                period = parsed;
            }
            else
            {
                throw new ArgumentException(
                    $"RelativePeriod operator requires a RelativeDatePeriod value, got: {value?.GetType().Name ?? "null"}.");
            }
        }

        var propertyType = Nullable.GetUnderlyingType(property.Type) ?? property.Type;
        if (propertyType != typeof(DateTime) && propertyType != typeof(DateTimeOffset))
        {
            throw new InvalidOperationException(
                $"RelativePeriod operator requires a DateTime or DateTimeOffset property, but got '{propertyType.Name}'.");
        }

        var (start, end) = RelativeDatePeriodResolver.Resolve(period, DateTimeOffset.UtcNow);
        Expression accessProperty = property;

        // For nullable DateTime, access .Value for comparison
        if (Nullable.GetUnderlyingType(property.Type) is not null)
        {
            var hasValue = Expression.Property(property, "HasValue");
            var valueAccess = Expression.Property(property, "Value");

            Expression startConst, endConst;
            if (propertyType == typeof(DateTimeOffset))
            {
                startConst = Expression.Constant(start, typeof(DateTimeOffset));
                endConst = Expression.Constant(end, typeof(DateTimeOffset));
            }
            else
            {
                startConst = Expression.Constant(start.UtcDateTime, typeof(DateTime));
                endConst = Expression.Constant(end.UtcDateTime, typeof(DateTime));
            }

            var range = Expression.AndAlso(
                Expression.GreaterThanOrEqual(valueAccess, startConst),
                Expression.LessThanOrEqual(valueAccess, endConst));

            return Expression.AndAlso(hasValue, range);
        }

        Expression startConstant, endConstant;
        if (propertyType == typeof(DateTimeOffset))
        {
            startConstant = Expression.Constant(start, typeof(DateTimeOffset));
            endConstant = Expression.Constant(end, typeof(DateTimeOffset));
        }
        else
        {
            startConstant = Expression.Constant(start.UtcDateTime, typeof(DateTime));
            endConstant = Expression.Constant(end.UtcDateTime, typeof(DateTime));
        }

        return Expression.AndAlso(
            Expression.GreaterThanOrEqual(accessProperty, startConstant),
            Expression.LessThanOrEqual(accessProperty, endConstant));
    }

    private static Expression BuildComparison(
        Expression property,
        object? value,
        Func<Expression, Expression, Expression> comparison)
    {
        var constant = ConvertConstant(value, property.Type);
        return comparison(property, constant);
    }

    private static BinaryExpression BuildBetween(Expression property, object? valueStart, object? valueEnd)
    {
        if (valueStart is null || valueEnd is null)
        {
            throw new ArgumentException("Between operator requires both Value and ValueEnd.");
        }

        var start = ConvertConstant(valueStart, property.Type);
        var end = ConvertConstant(valueEnd, property.Type);

        return Expression.AndAlso(
            Expression.GreaterThanOrEqual(property, start),
            Expression.LessThanOrEqual(property, end));
    }

    private static Expression BuildIn(Expression property, object? value)
    {
        if (value is not IEnumerable enumerable)
        {
            throw new ArgumentException("In operator requires an IEnumerable value.");
        }

        var values = enumerable.Cast<object>().ToList();
        if (values.Count == 0)
        {
            return Expression.Constant(false);
        }

        Expression? result = null;
        foreach (var item in values)
        {
            var constant = ConvertConstant(item, property.Type);
            var equals = Expression.Equal(property, constant);
            result = result is null ? equals : Expression.OrElse(result, equals);
        }

        return result!;
    }

    private static Expression BuildIsNull(Expression property)
    {
        if (property.Type.IsValueType && Nullable.GetUnderlyingType(property.Type) is null)
        {
            // Non-nullable value type can never be null
            return Expression.Constant(false);
        }

        return Expression.Equal(property, Expression.Constant(null, property.Type));
    }

    private static ConstantExpression ConvertConstant(object? value, Type targetType)
    {
        if (value is null)
        {
            return Expression.Constant(null, targetType);
        }

        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        // Convert.ChangeType cannot turn a string into a CLR enum; route enum
        // targets through Enum.Parse so callers can pass the enum name (the
        // shape produced by ColumnRegistryBase.EnumColumn<TEnum>() and by
        // inline column filter pickers).
        if (underlyingType.IsEnum)
        {
            object converted = value switch
            {
                string s => Enum.Parse(underlyingType, s, ignoreCase: true),
                _ when value.GetType() == underlyingType => value,
                _ => Enum.ToObject(underlyingType, value),
            };
            return Expression.Constant(converted, targetType);
        }

        var changed = Convert.ChangeType(value, underlyingType, CultureInfo.InvariantCulture);
        return Expression.Constant(changed, targetType);
    }
}
