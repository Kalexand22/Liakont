namespace Liakont.Modules.TenantSettings.Contracts.DTOs;

/// <summary>
/// Résumé d'état de la table de mapping TVA du tenant, exposé par <c>GET /api/v1/settings</c> (API01c) :
/// version, validateur et état de validation (l'écran de paramétrage signale une table « NON VALIDÉE »).
/// Projection LOCALE (pas <c>MappingTableDto</c> du module TvaMapping) — la surface Contracts du module
/// TenantSettings ne dépend que de Common (frontière). Le DÉTAIL et l'ÉDITION de la table relèvent
/// d'API04 ; ici, seul l'état est résumé. Aucune règle fiscale n'est interprétée (CLAUDE.md n°2).
/// </summary>
public record TvaMappingSummaryDto
{
    /// <summary>Version de la table.</summary>
    public required string MappingVersion { get; init; }

    /// <summary><c>true</c> si la table a été validée humainement ; <c>false</c> = « NON VALIDÉE » (item TVA01 §5).</summary>
    public required bool IsValidated { get; init; }

    /// <summary>Identité du valideur (expert-comptable), <c>null</c> si non validée.</summary>
    public string? ValidatedBy { get; init; }

    /// <summary>Date de validation humaine, <c>null</c> si non validée.</summary>
    public DateOnly? ValidatedDate { get; init; }

    /// <summary>Comportement par défaut pour un régime non mappé (toujours <c>Block</c>, F03 §4.1).</summary>
    public required string DefaultBehavior { get; init; }

    /// <summary>Nombre de règles de mapping de la table (le détail des règles est exposé par API04).</summary>
    public required int RuleCount { get; init; }
}
