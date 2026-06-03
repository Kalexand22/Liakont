namespace Stratum.Common.Abstractions.FieldChange;

using Stratum.Common.Abstractions.Security;

/// <summary>
/// Context passed to an <see cref="OnChangeAttribute"/>-decorated handler method
/// when a field value changes.
/// </summary>
/// <typeparam name="T">The entity type whose field changed.</typeparam>
public sealed record FieldChangeContext<T>
{
    public required T Entity { get; init; }

    public required string FieldName { get; init; }

    public object? PreviousValue { get; init; }

    public required IActorContext Actor { get; init; }

    public CancellationToken CancellationToken { get; init; }
}
