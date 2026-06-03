namespace Stratum.Modules.Identity.Domain.Entities;

using Stratum.Modules.Identity.Domain.Services;

/// <summary>
/// Represents a permission grant on a Role.
/// Permission format: {module}.{action} or {module}.{entity}.{action}
/// An optional ABAC condition restricts grant applicability at runtime.
/// </summary>
public sealed class Grant
{
    private Grant()
    {
    }

    public Guid Id { get; private set; }

    public Guid RoleId { get; private set; }

    public string Permission { get; private set; } = null!;

    public string ModuleSource { get; private set; } = null!;

    public string? Condition { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Grant Create(Guid roleId, string permission, string moduleSource, string? condition = null)
    {
        if (string.IsNullOrWhiteSpace(permission))
        {
            throw new ArgumentException("Permission cannot be null or empty.", nameof(permission));
        }

        if (string.IsNullOrWhiteSpace(moduleSource))
        {
            throw new ArgumentException("ModuleSource cannot be null or empty.", nameof(moduleSource));
        }

        if (condition is not null)
        {
            var parseResult = ConditionParser.Validate(condition);
            if (!parseResult.IsValid)
            {
                throw new ArgumentException(
                    $"INV-IDENT-016: Invalid condition syntax: {parseResult.ErrorMessage}",
                    nameof(condition));
            }
        }

        return new Grant
        {
            Id = Guid.NewGuid(),
            RoleId = roleId,
            Permission = permission,
            ModuleSource = moduleSource,
            Condition = condition,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public static Grant Reconstitute(
        Guid id,
        Guid roleId,
        string permission,
        string moduleSource,
        string? condition,
        DateTimeOffset createdAt)
    {
        return new Grant
        {
            Id = id,
            RoleId = roleId,
            Permission = permission,
            ModuleSource = moduleSource,
            Condition = condition,
            CreatedAt = createdAt,
        };
    }
}
