namespace Stratum.Common.Abstractions.Display;

/// <summary>
/// Defines how a related entity is displayed in grids, forms, and lookups.
/// Each module registers display templates for its entity types at startup.
/// </summary>
/// <typeparam name="TEntity">The entity or DTO type to format.</typeparam>
public interface IDisplayTemplate<in TEntity>
{
    /// <summary>
    /// Formats the entity for display (e.g. "P-001 — Acme Corp").
    /// </summary>
    string Format(TEntity entity);
}
