namespace Stratum.Common.UI.Helpers;

/// <summary>
/// Pure helper for TextArea character count display.
/// Kept static and side-effect-free for easy unit testing.
/// </summary>
public static class TextAreaCharCount
{
    /// <summary>
    /// Formats the character count string: "len / max" or "len" when no max.
    /// </summary>
    public static string Format(string? value, int? maxLength)
    {
        var len = value?.Length ?? 0;
        return maxLength.HasValue
            ? $"{len} / {maxLength.Value}"
            : len.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Returns <c>true</c> when the value exceeds the maximum length.
    /// Always <c>false</c> when <paramref name="maxLength"/> is <c>null</c>.
    /// </summary>
    public static bool IsOverLimit(string? value, int? maxLength)
    {
        if (maxLength is null)
        {
            return false;
        }

        return (value?.Length ?? 0) > maxLength.Value;
    }
}
