namespace Stratum.Modules.Identity.Domain.ValueObjects;

using System.Text.RegularExpressions;

/// <summary>
/// Username value object. INV-IDENTITY-007: 3-50 chars, alphanumeric + underscores only.
/// </summary>
public sealed class Username
{
    private static readonly Regex ValidPattern = new(@"^[a-zA-Z0-9_]{3,50}$", RegexOptions.Compiled);

    private Username(string value) => Value = value;

    public string Value { get; }

    public static Username From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Username cannot be null or empty. (INV-IDENTITY-007)", nameof(value));
        }

        if (!ValidPattern.IsMatch(value))
        {
            throw new ArgumentException(
                $"Username '{value}' is invalid. Must be 3-50 characters, alphanumeric and underscores only. (INV-IDENTITY-007)",
                nameof(value));
        }

        return new Username(value);
    }

    public override string ToString() => Value;

    public override bool Equals(object? obj) => obj is Username other && Value == other.Value;

    public override int GetHashCode() => Value.GetHashCode(StringComparison.OrdinalIgnoreCase);
}
