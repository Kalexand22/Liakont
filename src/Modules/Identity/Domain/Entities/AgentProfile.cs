namespace Stratum.Modules.Identity.Domain.Entities;

public sealed class AgentProfile
{
    private AgentProfile()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string? ServiceCode { get; private set; }

    public string? Title { get; private set; }

    public string? Phone { get; private set; }

    public string? OfficeLocation { get; private set; }

    public DateOnly? HireDate { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public static AgentProfile Create(
        Guid userId,
        string? serviceCode,
        string? title,
        string? phone,
        string? officeLocation,
        DateOnly? hireDate,
        string? notes)
    {
        return new AgentProfile
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ServiceCode = serviceCode,
            Title = title,
            Phone = phone,
            OfficeLocation = officeLocation,
            HireDate = hireDate,
            Notes = notes,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public static AgentProfile Reconstitute(
        Guid id,
        Guid userId,
        string? serviceCode,
        string? title,
        string? phone,
        string? officeLocation,
        DateOnly? hireDate,
        string? notes,
        DateTimeOffset createdAt,
        DateTimeOffset? updatedAt)
    {
        return new AgentProfile
        {
            Id = id,
            UserId = userId,
            ServiceCode = serviceCode,
            Title = title,
            Phone = phone,
            OfficeLocation = officeLocation,
            HireDate = hireDate,
            Notes = notes,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
        };
    }

    public void Update(
        string? serviceCode,
        string? title,
        string? phone,
        string? officeLocation,
        DateOnly? hireDate,
        string? notes)
    {
        ServiceCode = serviceCode;
        Title = title;
        Phone = phone;
        OfficeLocation = officeLocation;
        HireDate = hireDate;
        Notes = notes;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
