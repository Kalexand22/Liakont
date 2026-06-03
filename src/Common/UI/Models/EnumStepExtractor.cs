namespace Stratum.Common.UI.Models;

using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

/// <summary>
/// Extracts <see cref="EnumStepInfo"/> metadata from enum types, with caching.
/// </summary>
internal static partial class EnumStepExtractor
{
    private static readonly ConcurrentDictionary<Type, IReadOnlyList<EnumStepInfo>> Cache = new();

    /// <summary>
    /// Returns step metadata for all values of <typeparamref name="TEnum"/>, ordered by value.
    /// Results are cached per enum type.
    /// </summary>
    internal static IReadOnlyList<EnumStepInfo> GetSteps<TEnum>()
        where TEnum : struct, Enum
        => Cache.GetOrAdd(typeof(TEnum), _ => ExtractSteps<TEnum>());

    /// <summary>Converts PascalCase to spaced words: "InProgress" → "In Progress".</summary>
    internal static string Humanize(string pascalCase)
        => HumanizeRegex().Replace(pascalCase, " $1").Trim();

    private static ReadOnlyCollection<EnumStepInfo> ExtractSteps<TEnum>()
        where TEnum : struct, Enum
    {
        var values = Enum.GetValues<TEnum>();
        var steps = new List<EnumStepInfo>(values.Length);
        var ordinal = 0;

        // DistinctBy filters out aliased enum values (multiple names mapping to the same integer)
        foreach (var value in values
            .DistinctBy(v => Convert.ToInt32(v, CultureInfo.InvariantCulture))
            .OrderBy(v => Convert.ToInt32(v, CultureInfo.InvariantCulture)))
        {
            var name = value.ToString();
            var field = typeof(TEnum).GetField(name)!;

            var displayAttr = field.GetCustomAttribute<DisplayAttribute>();
            var iconAttr = field.GetCustomAttribute<StepIconAttribute>();
            var colorAttr = field.GetCustomAttribute<StepColorAttribute>();

            steps.Add(new EnumStepInfo
            {
                Ordinal = ordinal++,
                Label = displayAttr?.Name ?? Humanize(name),
                Icon = iconAttr?.IconName,
                Color = colorAttr?.ColorName,
                Value = Convert.ToInt32(value, CultureInfo.InvariantCulture),
            });
        }

        return steps.AsReadOnly();
    }

    [GeneratedRegex(@"(?<=[a-z])([A-Z])")]
    private static partial Regex HumanizeRegex();
}
