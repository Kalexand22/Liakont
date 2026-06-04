namespace Liakont.Modules.TvaMapping.Contracts.DTOs;

/// <summary>
/// Table de mapping TVA d'un tenant en lecture (F03 §4.1, item TVA01). <see cref="IsValidated"/>
/// porte l'état « NON VALIDÉE » (item TVA01 §5) consommé par la console, la supervision et le
/// garde-fou d'envoi en production. Lecture tenant-scopée uniquement (CLAUDE.md n°9/17).
/// </summary>
public record MappingTableDto
{
    /// <summary>Identifiant technique de la table.</summary>
    public required Guid Id { get; init; }

    /// <summary>Tenant propriétaire.</summary>
    public required Guid CompanyId { get; init; }

    /// <summary>Version de la table.</summary>
    public required string MappingVersion { get; init; }

    /// <summary>Identité du valideur (expert-comptable), <c>null</c> si non validée.</summary>
    public string? ValidatedBy { get; init; }

    /// <summary>Date de validation humaine, <c>null</c> si non validée.</summary>
    public DateOnly? ValidatedDate { get; init; }

    /// <summary>
    /// <c>true</c> si la table a été validée humainement (<see cref="ValidatedBy"/> et
    /// <see cref="ValidatedDate"/> renseignés) ; <c>false</c> = « NON VALIDÉE ».
    /// </summary>
    public required bool IsValidated { get; init; }

    /// <summary>Comportement par défaut (régime non mappé), toujours <c>Block</c> (F03 §4.1).</summary>
    public required string DefaultBehavior { get; init; }

    /// <summary>Règles de mapping, dans l'ordre de déclaration.</summary>
    public required IReadOnlyList<MappingRuleDto> Rules { get; init; }

    /// <summary>Date de création (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Date de dernière modification (UTC), <c>null</c> si jamais modifiée.</summary>
    public DateTimeOffset? UpdatedAt { get; init; }
}
