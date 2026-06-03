namespace Stratum.Modules.Notification.Domain.Entities;

using Stratum.Modules.Notification.Domain.ValueObjects;

public sealed class RoutingRule
{
    private RoutingRule()
    {
    }

    public Guid Id { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string EntityType { get; private set; } = string.Empty;

    public string ServiceCode { get; private set; } = string.Empty;

    public RecipientType RecipientType { get; private set; }

    public string RecipientValue { get; private set; } = string.Empty;

    public IReadOnlyList<RoutingCondition> Conditions { get; private set; } = [];

    public int Priority { get; private set; }

    public bool IsActive { get; private set; }

    public Guid? CompanyId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public static RoutingRule Create(
        string code,
        string name,
        string entityType,
        string serviceCode,
        RecipientType recipientType,
        string recipientValue,
        IReadOnlyList<RoutingCondition> conditions,
        int priority,
        Guid? companyId)
    {
        ValidateCode(code);
        ValidateName(name);
        ValidateEntityType(entityType);
        ValidateServiceCode(serviceCode);
        ValidateRecipientValue(recipientValue);

        return new RoutingRule
        {
            Id = Guid.NewGuid(),
            Code = code.Trim().ToLowerInvariant(),
            Name = name.Trim(),
            EntityType = entityType.Trim().ToLowerInvariant(),
            ServiceCode = serviceCode.Trim().ToLowerInvariant(),
            RecipientType = recipientType,
            RecipientValue = recipientValue.Trim(),
            Conditions = conditions ?? [],
            Priority = priority,
            IsActive = true,
            CompanyId = companyId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public static RoutingRule Reconstitute(
        Guid id,
        string code,
        string name,
        string entityType,
        string serviceCode,
        RecipientType recipientType,
        string recipientValue,
        IReadOnlyList<RoutingCondition> conditions,
        int priority,
        bool isActive,
        Guid? companyId,
        DateTimeOffset createdAt,
        DateTimeOffset? updatedAt)
    {
        return new RoutingRule
        {
            Id = id,
            Code = code,
            Name = name,
            EntityType = entityType,
            ServiceCode = serviceCode,
            RecipientType = recipientType,
            RecipientValue = recipientValue,
            Conditions = conditions,
            Priority = priority,
            IsActive = isActive,
            CompanyId = companyId,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
        };
    }

    public void Update(
        string name,
        string serviceCode,
        RecipientType recipientType,
        string recipientValue,
        IReadOnlyList<RoutingCondition> conditions,
        int priority,
        bool isActive)
    {
        ValidateName(name);
        ValidateServiceCode(serviceCode);
        ValidateRecipientValue(recipientValue);

        Name = name.Trim();
        ServiceCode = serviceCode.Trim().ToLowerInvariant();
        RecipientType = recipientType;
        RecipientValue = recipientValue.Trim();
        Conditions = conditions ?? [];
        Priority = priority;
        IsActive = isActive;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static void ValidateCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("INV-NOTIF-010: RoutingRule code must not be empty.", nameof(code));
        }
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("RoutingRule name must not be empty.", nameof(name));
        }
    }

    private static void ValidateEntityType(string entityType)
    {
        if (string.IsNullOrWhiteSpace(entityType))
        {
            throw new ArgumentException("RoutingRule entityType must not be empty.", nameof(entityType));
        }
    }

    private static void ValidateServiceCode(string serviceCode)
    {
        if (string.IsNullOrWhiteSpace(serviceCode))
        {
            throw new ArgumentException("INV-NOTIF-012: RoutingRule serviceCode must not be empty.", nameof(serviceCode));
        }
    }

    private static void ValidateRecipientValue(string recipientValue)
    {
        if (string.IsNullOrWhiteSpace(recipientValue))
        {
            throw new ArgumentException("RoutingRule recipientValue must not be empty.", nameof(recipientValue));
        }
    }
}
