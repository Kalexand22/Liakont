namespace Stratum.Common.Abstractions.Display;

/// <summary>
/// Resolves <see cref="IDisplayTemplate{TEntity}"/> instances for any entity type.
/// Falls back to <see cref="object.ToString"/> when no template is registered.
/// </summary>
public interface IDisplayTemplateRegistry
{
    /// <summary>
    /// Formats an entity using its registered display template, or ToString() as fallback.
    /// </summary>
    string Format<TEntity>(TEntity entity)
        where TEntity : notnull;

    /// <summary>
    /// Returns true if a display template is registered for <typeparamref name="TEntity"/>.
    /// </summary>
    bool HasTemplate<TEntity>();

    /// <summary>
    /// Gets the display template for <typeparamref name="TEntity"/>, or null if none registered.
    /// </summary>
    IDisplayTemplate<TEntity>? GetTemplate<TEntity>();

    /// <summary>
    /// Formats an entity whose type is only known at runtime.
    /// Resolves the display template by the entity's runtime type and falls back to ToString().
    /// </summary>
    string FormatObject(object entity);
}
