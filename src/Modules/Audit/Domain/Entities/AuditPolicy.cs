namespace Stratum.Modules.Audit.Domain.Entities;

public sealed class AuditPolicy
{
    private AuditPolicy()
    {
    }

    public Guid Id { get; private set; }

    public string EntityType { get; private set; } = string.Empty;

    public string ModuleSource { get; private set; } = string.Empty;

    public bool IsEnabled { get; private set; }

    public IReadOnlyList<string> TrackedFields { get; private set; } = [];

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public static AuditPolicy Create(
        string entityType,
        string moduleSource,
        bool isEnabled,
        IReadOnlyList<string> trackedFields)
    {
        ValidateEntityType(entityType);
        ValidateModuleSource(moduleSource);

        return new AuditPolicy
        {
            Id = Guid.NewGuid(),
            EntityType = entityType,
            ModuleSource = moduleSource,
            IsEnabled = isEnabled,
            TrackedFields = trackedFields,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public static AuditPolicy Reconstitute(
        Guid id,
        string entityType,
        string moduleSource,
        bool isEnabled,
        IReadOnlyList<string> trackedFields,
        DateTimeOffset createdAt,
        DateTimeOffset? updatedAt)
    {
        return new AuditPolicy
        {
            Id = id,
            EntityType = entityType,
            ModuleSource = moduleSource,
            IsEnabled = isEnabled,
            TrackedFields = trackedFields,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
        };
    }

    public void Update(
        string moduleSource,
        bool isEnabled,
        IReadOnlyList<string> trackedFields)
    {
        ValidateModuleSource(moduleSource);

        ModuleSource = moduleSource;
        IsEnabled = isEnabled;
        TrackedFields = trackedFields;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Disable()
    {
        IsEnabled = false;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static void ValidateEntityType(string entityType)
    {
        if (string.IsNullOrWhiteSpace(entityType))
        {
            throw new ArgumentException("INV-AUDIT-003: entity_type must not be empty.", nameof(entityType));
        }
    }

    private static void ValidateModuleSource(string moduleSource)
    {
        if (string.IsNullOrWhiteSpace(moduleSource))
        {
            throw new ArgumentException("module_source must not be empty.", nameof(moduleSource));
        }
    }
}
