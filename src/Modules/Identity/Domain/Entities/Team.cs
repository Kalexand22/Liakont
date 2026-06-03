namespace Stratum.Modules.Identity.Domain.Entities;

public sealed class Team
{
    private Team()
    {
    }

    public Guid Id { get; private set; }

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public string? Description { get; private set; }

    public string? ServiceCode { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public static Team Create(string code, string name, string? description, string? serviceCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new Team
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = name,
            Description = description,
            ServiceCode = serviceCode,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public static Team Reconstitute(
        Guid id,
        string code,
        string name,
        string? description,
        string? serviceCode,
        bool isActive,
        DateTimeOffset createdAt,
        DateTimeOffset? updatedAt)
    {
        return new Team
        {
            Id = id,
            Code = code,
            Name = name,
            Description = description,
            ServiceCode = serviceCode,
            IsActive = isActive,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
        };
    }

    public void Update(string name, string? description, string? serviceCode, bool isActive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Description = description;
        ServiceCode = serviceCode;
        IsActive = isActive;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
