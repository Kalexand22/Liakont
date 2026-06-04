namespace Liakont.Modules.TenantSettings.Contracts.DTOs;

/// <summary>Seuils d'alerte de supervision en lecture (F12-A §6).</summary>
public record AlertThresholdsDto
{
    public required Guid Id { get; init; }

    public required Guid CompanyId { get; init; }

    public required int AgentSilentHours { get; init; }

    public required int MissedRunHours { get; init; }

    public required int PushQueueMaxItems { get; init; }

    public required int PushQueueMaxAgeHours { get; init; }

    public required int BlockedDocumentsDays { get; init; }

    public required int PaRejectionsDays { get; init; }

    public required bool AlertTenantContact { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
}
