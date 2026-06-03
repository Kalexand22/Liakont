namespace Stratum.Common.Abstractions.Gis;

/// <summary>
/// A single property definition within a feature type.
/// </summary>
public sealed record FeatureProperty
{
    public required string Name { get; init; }

    public required string Type { get; init; }

    public bool IsNullable { get; init; } = true;
}
