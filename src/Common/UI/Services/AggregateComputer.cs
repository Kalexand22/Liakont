namespace Stratum.Common.UI.Services;

using System.Globalization;
using System.Reflection;
using Stratum.Common.UI.Models;

/// <summary>
/// Computes aggregate values (Sum, Average, Count, Min, Max) for a given
/// property on a collection of items. Used by StratumDataGrid footer rendering.
/// </summary>
internal static class AggregateComputer
{
    /// <summary>
    /// Compute an aggregate value for the specified property on the given data.
    /// Returns the formatted result string, or null if computation is not possible.
    /// </summary>
    public static string? Compute<TItem>(
        IReadOnlyList<TItem> data,
        string property,
        AggregateFunc func,
        string? format = null)
    {
        if (data.Count == 0 || func == AggregateFunc.None || string.IsNullOrEmpty(property))
        {
            return null;
        }

        var propInfo = GetPropertyInfo(typeof(TItem), property);
        if (propInfo is null)
        {
            return null;
        }

        if (func == AggregateFunc.Count)
        {
            var count = CountNonNull(data, property);
            return FormatValue(count, format);
        }

        var values = ExtractDecimalValues(data, property);
        if (values.Count == 0)
        {
            return null;
        }

        var result = func switch
        {
            AggregateFunc.Sum => values.Sum(),
            AggregateFunc.Average => values.Average(),
            AggregateFunc.Min => values.Min(),
            AggregateFunc.Max => values.Max(),
            _ => (decimal?)null,
        };

        return result.HasValue ? FormatValue(result.Value, format) : null;
    }

    /// <summary>
    /// Get the label prefix for an aggregate function (e.g. "Sum", "Avg").
    /// </summary>
    public static string GetLabel(AggregateFunc func) => func switch
    {
        AggregateFunc.Sum => "Sum",
        AggregateFunc.Average => "Avg",
        AggregateFunc.Count => "Count",
        AggregateFunc.Min => "Min",
        AggregateFunc.Max => "Max",
        _ => string.Empty,
    };

    private static PropertyInfo? GetPropertyInfo(Type type, string propertyPath)
    {
        var currentType = type;
        PropertyInfo? result = null;

        foreach (var segment in propertyPath.Split('.'))
        {
            result = currentType.GetProperty(segment, BindingFlags.Public | BindingFlags.Instance);
            if (result is null)
            {
                return null;
            }

            currentType = result.PropertyType;
        }

        return result;
    }

    private static int CountNonNull<TItem>(IReadOnlyList<TItem> data, string propertyPath)
    {
        var count = 0;
        foreach (var item in data)
        {
            if (item is null)
            {
                continue;
            }

            var val = NavigatePropertyPath(item, propertyPath);
            if (val is not null)
            {
                count++;
            }
        }

        return count;
    }

    private static List<decimal> ExtractDecimalValues<TItem>(IReadOnlyList<TItem> data, string propertyPath)
    {
        var values = new List<decimal>(data.Count);

        foreach (var item in data)
        {
            if (item is null)
            {
                continue;
            }

            var raw = NavigatePropertyPath(item, propertyPath);
            if (raw is null)
            {
                continue;
            }

            if (TryConvertToDecimal(raw, out var d))
            {
                values.Add(d);
            }
        }

        return values;
    }

    private static object? NavigatePropertyPath(object? obj, string path)
    {
        if (obj is null || string.IsNullOrEmpty(path))
        {
            return null;
        }

        var current = obj;
        foreach (var segment in path.Split('.'))
        {
            if (current is null)
            {
                return null;
            }

            var prop = current.GetType().GetProperty(segment, BindingFlags.Public | BindingFlags.Instance);
            if (prop is null)
            {
                return null;
            }

            current = prop.GetValue(current);
        }

        return current;
    }

    private static bool TryConvertToDecimal(object value, out decimal result)
    {
        result = 0m;
        try
        {
            result = value switch
            {
                int i => i,
                long l => l,
                float f => (decimal)f,
                double d => (decimal)d,
                decimal m => m,
                short s => s,
                byte b => b,
                _ => Convert.ToDecimal(value, CultureInfo.InvariantCulture),
            };
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (InvalidCastException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static string FormatValue(object value, string? format)
    {
        if (format is not null && value is IFormattable formattable)
        {
            return formattable.ToString(format, CultureInfo.CurrentCulture);
        }

        return value.ToString() ?? string.Empty;
    }
}
