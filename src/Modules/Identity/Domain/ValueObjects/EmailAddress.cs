namespace Stratum.Modules.Identity.Domain.ValueObjects;

using System.Text.RegularExpressions;

/// <summary>
/// EmailAddress value object with basic format validation.
/// </summary>
public sealed class EmailAddress
{
    private static readonly Regex ValidPattern = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private EmailAddress(string value) => Value = value;

    public string Value { get; }

    public static EmailAddress From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Email address cannot be null or empty.", nameof(value));
        }

        if (!ValidPattern.IsMatch(value))
        {
            throw new ArgumentException(
                $"Email address '{value}' is not a valid email format.",
                nameof(value));
        }

        return new EmailAddress(value.ToLowerInvariant());
    }

    public override string ToString() => Value;

    public override bool Equals(object? obj) => obj is EmailAddress other && Value == other.Value;

    public override int GetHashCode() => Value.GetHashCode(StringComparison.OrdinalIgnoreCase);
}
