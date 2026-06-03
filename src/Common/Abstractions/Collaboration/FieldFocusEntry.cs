namespace Stratum.Common.Abstractions.Collaboration;

/// <summary>
/// Represents a single user's focus on a specific field.
/// </summary>
public sealed record FieldFocusEntry(string CircuitId, string User, DateTimeOffset FocusedAt);
