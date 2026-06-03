namespace Stratum.Common.UI.Helpers;

/// <summary>
/// Parses free-form monetary text input into a <see cref="decimal"/>.
/// Supports EU (1.234,56) and US/invariant (1,234.56) formats.
/// Extracted to a testable class — used internally by <c>MoneyField</c>.
/// </summary>
public static class MoneyParser
{
    /// <summary>
    /// Attempts to parse a free-form monetary string.
    /// Rules:
    /// <list type="bullet">
    ///   <item>Leading/trailing whitespace and non-breaking spaces are stripped.</item>
    ///   <item>One separator type: comma-only → comma is decimal. Dot-only → dot is decimal.</item>
    ///   <item>Mixed separators: whichever appears last is the decimal separator.</item>
    ///   <item>Multiple same-separator: all are thousands separators, no decimal part.</item>
    /// </list>
    /// </summary>
    public static bool TryParse(string text, out decimal result)
    {
        var s = text.Replace(" ", string.Empty).Replace("\u00a0", string.Empty);

        var commaCount = s.Count(c => c == ',');
        var dotCount = s.Count(c => c == '.');

        string normalized;

        if (commaCount == 1 && dotCount == 0)
        {
            // "1234,56" — comma is decimal separator.
            normalized = s.Replace(',', '.');
        }
        else if (commaCount == 0 && dotCount == 1)
        {
            // "1234.56" — dot is decimal separator (already Invariant-compatible).
            normalized = s;
        }
        else if (commaCount >= 1 && dotCount >= 1)
        {
            // Mixed: whichever separator comes last is the decimal.
            var lastComma = s.LastIndexOf(',');
            var lastDot = s.LastIndexOf('.');
            if (lastComma > lastDot)
            {
                // "1.234,56" — dot = thousands, comma = decimal.
                normalized = s.Replace(".", string.Empty).Replace(',', '.');
            }
            else
            {
                // "1,234.56" — comma = thousands, dot = decimal.
                normalized = s.Replace(",", string.Empty);
            }
        }
        else if (commaCount > 1)
        {
            // "1,234,567" — commas are thousands separators, no decimal part.
            normalized = s.Replace(",", string.Empty);
        }
        else if (dotCount > 1)
        {
            // "1.234.567" — dots are thousands separators, no decimal part.
            normalized = s.Replace(".", string.Empty);
        }
        else
        {
            normalized = s;
        }

        return decimal.TryParse(
            normalized,
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture,
            out result);
    }
}
