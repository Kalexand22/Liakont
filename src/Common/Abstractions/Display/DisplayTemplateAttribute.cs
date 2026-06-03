namespace Stratum.Common.Abstractions.Display;

/// <summary>
/// Declares the default <see cref="IDisplayTemplate{TEntity}"/> implementation for an entity or DTO.
/// Applied to the entity/DTO type to enable automatic discovery by the registry.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DisplayTemplateAttribute : Attribute
{
    public DisplayTemplateAttribute(Type templateType)
    {
        TemplateType = templateType ?? throw new ArgumentNullException(nameof(templateType));
    }

    /// <summary>
    /// The type implementing <see cref="IDisplayTemplate{TEntity}"/> for the decorated entity.
    /// </summary>
    public Type TemplateType { get; }
}
