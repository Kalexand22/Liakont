namespace Liakont.Modules.TenantSettings.Contracts.DTOs;

/// <summary>
/// Paramétrage fiscal en lecture (F12-A §3). Tous les champs sont nullables : <c>null</c> = décision
/// de l'expert-comptable en attente = transmissions concernées suspendues (jamais de défaut).
/// <see cref="ReportingFrequency"/> est une chaîne opaque (énumération non figée — F12-A §3.3).
/// </summary>
public record FiscalSettingsDto
{
    public required Guid Id { get; init; }

    public required Guid CompanyId { get; init; }

    public bool? VatOnDebits { get; init; }

    public string? OperationCategory { get; init; }

    public string? ReportingFrequency { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
}
