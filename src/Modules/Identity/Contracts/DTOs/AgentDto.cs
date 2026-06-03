namespace Stratum.Modules.Identity.Contracts.DTOs;

public record AgentDto
{
    public required Guid Id { get; init; }

    public required Guid UserId { get; init; }

    public required string Username { get; init; }

    public required string Email { get; init; }

    public string? DisplayName { get; init; }

    public string? ServiceCode { get; init; }

    public string? Title { get; init; }

    public string? Phone { get; init; }

    public string? OfficeLocation { get; init; }

    public DateOnly? HireDate { get; init; }

    public string? Notes { get; init; }

    public bool IsActive { get; init; }

    public string? Teams { get; init; }

    public int CompetenceCount { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
}
