namespace Stratum.Common.Abstractions.Collaboration;

/// <summary>
/// Marks a command as triggering collaborative-editing notifications.
/// The <see cref="EntityChangedBehavior{TRequest,TResponse}"/> reads this attribute
/// to broadcast an <see cref="EntityChangedEvent"/> after successful execution.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EntityChangeAttribute : Attribute
{
    public EntityChangeAttribute(string entityType, string entityIdProperty)
    {
        EntityType = entityType;
        EntityIdProperty = entityIdProperty;
    }

    /// <summary>Entity type identifier (e.g. "Product", "Quote").</summary>
    public string EntityType { get; }

    /// <summary>Name of the property on the command that holds the entity ID (e.g. "ProductId").</summary>
    public string EntityIdProperty { get; }
}
