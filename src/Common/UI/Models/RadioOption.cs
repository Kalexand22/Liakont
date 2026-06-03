namespace Stratum.Common.UI.Models;

/// <summary>
/// A single option in a <c>RadioGroup&lt;TValue&gt;</c>.
/// </summary>
/// <typeparam name="TValue">The value type of the option.</typeparam>
/// <param name="Value">The backing value bound when selected.</param>
/// <param name="Label">Visible label text for the option.</param>
/// <param name="Disabled">Whether this individual option is disabled.</param>
public sealed record RadioOption<TValue>(TValue Value, string Label, bool Disabled = false);
