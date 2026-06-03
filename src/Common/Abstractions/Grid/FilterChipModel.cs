namespace Stratum.Common.Abstractions.Grid;

/// <summary>
/// Read-only projection of an active filter for display as a chip.
/// Produced by <see cref="FilterChipProjector"/>.
/// </summary>
/// <param name="Label">Display text (e.g. "Service: finances", "Montant > 5 000").</param>
/// <param name="Source">Origin of the filter (Simple, Advanced, GlobalSearch).</param>
/// <param name="Criterion">
/// The criterion for simple/advanced-explicit chips. Null for global search
/// and advanced summary chips.
/// </param>
/// <param name="Group">
/// The parent FilterGroup for advanced chips. Null for simple and global search chips.
/// </param>
/// <param name="CanEdit">
/// Whether clicking the chip opens an editor. Always true for simple and global search;
/// true for advanced chips (opens StratumFilterBuilder).
/// </param>
public sealed record FilterChipModel(
    string Label,
    FilterSource Source,
    FilterCriterion? Criterion = null,
    FilterGroup? Group = null,
    bool CanEdit = true);
