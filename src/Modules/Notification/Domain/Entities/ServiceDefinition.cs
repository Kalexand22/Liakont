namespace Stratum.Modules.Notification.Domain.Entities;

public sealed class ServiceDefinition
{
    private ServiceDefinition()
    {
    }

    public Guid Id { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public bool IsActive { get; private set; }

    public Guid? CompanyId { get; private set; }

    public string? ManagerName { get; private set; }

    public int? DefaultSlaHours { get; private set; }

    public string? Color { get; private set; }

    public string? Competences { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public static ServiceDefinition Create(
        string code,
        string name,
        string email,
        string? description,
        Guid? companyId,
        string? managerName = null,
        int? defaultSlaHours = null,
        string? color = null,
        string? competences = null)
    {
        ValidateCode(code);
        ValidateName(name);
        ValidateEmail(email);
        ValidateOptionalFields(managerName, defaultSlaHours, color);

        return new ServiceDefinition
        {
            Id = Guid.NewGuid(),
            Code = code.Trim().ToLowerInvariant(),
            Name = name.Trim(),
            Email = email.Trim(),
            Description = description?.Trim(),
            IsActive = true,
            CompanyId = companyId,
            ManagerName = managerName?.Trim(),
            DefaultSlaHours = defaultSlaHours,
            Color = color?.Trim(),
            Competences = competences?.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public static ServiceDefinition Reconstitute(
        Guid id,
        string code,
        string name,
        string email,
        string? description,
        bool isActive,
        Guid? companyId,
        DateTimeOffset createdAt,
        DateTimeOffset? updatedAt,
        string? managerName = null,
        int? defaultSlaHours = null,
        string? color = null,
        string? competences = null)
    {
        return new ServiceDefinition
        {
            Id = id,
            Code = code,
            Name = name,
            Email = email,
            Description = description,
            IsActive = isActive,
            CompanyId = companyId,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            ManagerName = managerName,
            DefaultSlaHours = defaultSlaHours,
            Color = color,
            Competences = competences,
        };
    }

    public void Update(
        string name,
        string email,
        string? description,
        bool isActive,
        string? managerName = null,
        int? defaultSlaHours = null,
        string? color = null,
        string? competences = null)
    {
        ValidateName(name);
        ValidateEmail(email);
        ValidateOptionalFields(managerName, defaultSlaHours, color);

        Name = name.Trim();
        Email = email.Trim();
        Description = description?.Trim();
        IsActive = isActive;
        ManagerName = managerName?.Trim();
        DefaultSlaHours = defaultSlaHours;
        Color = color?.Trim();
        Competences = competences?.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static void ValidateOptionalFields(string? managerName, int? defaultSlaHours, string? color)
    {
        if (managerName is not null && managerName.Length > 200)
        {
            throw new ArgumentException("Manager name must not exceed 200 characters.", nameof(managerName));
        }

        if (defaultSlaHours.HasValue && defaultSlaHours.Value < 0)
        {
            throw new ArgumentException("Default SLA hours must be zero or positive.", nameof(defaultSlaHours));
        }

        if (color is not null && color.Length > 20)
        {
            throw new ArgumentException("Color must not exceed 20 characters.", nameof(color));
        }
    }

    private static void ValidateCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("INV-NOTIF-011: ServiceDefinition code must not be empty.", nameof(code));
        }
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("ServiceDefinition name must not be empty.", nameof(name));
        }
    }

    private static void ValidateEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("ServiceDefinition email must not be empty.", nameof(email));
        }
    }
}
