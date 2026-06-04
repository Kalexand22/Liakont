namespace Liakont.Modules.TenantSettings.Contracts.DTOs;

/// <summary>Planification d'extraction en lecture (F12-A §5).</summary>
public record ExtractionScheduleDto
{
    public required Guid Id { get; init; }

    public required Guid CompanyId { get; init; }

    public required IReadOnlyList<string> Hours { get; init; }

    public required bool CatchUpOnStart { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
}
