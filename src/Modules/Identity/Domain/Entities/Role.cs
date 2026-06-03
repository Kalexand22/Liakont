namespace Stratum.Modules.Identity.Domain.Entities;

/// <summary>
/// Role aggregate root. INV-IDENTITY-004: system roles cannot be deleted or renamed.
/// </summary>
public sealed class Role
{
    private Role()
    {
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = null!;

    public string? Description { get; private set; }

    /// <summary>
    /// System roles (IsSystem=true) cannot be deleted or renamed. INV-IDENTITY-004.
    /// </summary>
    public bool IsSystem { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Role Create(string name, string? description, bool isSystem = false)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Role name cannot be null or empty.", nameof(name));
        }

        return new Role
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            IsSystem = isSystem,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public static Role Reconstitute(
        Guid id,
        string name,
        string? description,
        bool isSystem,
        DateTimeOffset createdAt)
    {
        return new Role
        {
            Id = id,
            Name = name,
            Description = description,
            IsSystem = isSystem,
            CreatedAt = createdAt,
        };
    }

    /// <summary>
    /// Renames this role. Throws if it is a system role. INV-IDENTITY-004.
    /// </summary>
    public void Rename(string newName)
    {
        if (IsSystem)
        {
            throw new InvalidOperationException($"System role '{Name}' cannot be renamed. (INV-IDENTITY-004)");
        }

        if (string.IsNullOrWhiteSpace(newName))
        {
            throw new ArgumentException("Role name cannot be null or empty.", nameof(newName));
        }

        Name = newName;
    }

    public void UpdateDescription(string? description)
    {
        if (IsSystem)
        {
            throw new InvalidOperationException($"System role '{Name}' cannot be modified. (INV-IDENTITY-004)");
        }

        Description = description;
    }

    public void EnsureDeletable()
    {
        if (IsSystem)
        {
            throw new InvalidOperationException($"System role '{Name}' cannot be deleted. (INV-IDENTITY-004)");
        }
    }
}
