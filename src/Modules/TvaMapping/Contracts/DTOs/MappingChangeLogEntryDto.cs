namespace Liakont.Modules.TvaMapping.Contracts.DTOs;

using System;

/// <summary>
/// Entrée du journal append-only des modifications de la table de mapping TVA (item TVA05 §3), exposée
/// en LECTURE pour la console (endpoint API04 GET /settings/tva-mapping, page WEB07). Projection du
/// journal <c>tvamapping.mapping_change_log</c> — JAMAIS de chemin d'écriture ici (le journal est
/// immuable, CLAUDE.md n°4 ; les écritures passent par le moteur d'édition TVA05). Les énumérations
/// sont exposées par leur NOM (string), comme le DTO de règle (<see cref="MappingRuleDto"/>).
/// </summary>
public sealed record MappingChangeLogEntryDto
{
    /// <summary>Identifiant de l'entrée de journal.</summary>
    public required Guid Id { get; init; }

    /// <summary>Nature de la modification (<c>AddRule</c> / <c>UpdateRule</c> / <c>RemoveRule</c> / <c>Validate</c>).</summary>
    public required string ChangeType { get; init; }

    /// <summary>Code régime de la règle concernée ; <c>null</c> pour une validation de table.</summary>
    public string? SourceRegimeCode { get; init; }

    /// <summary>Part de la règle concernée (<c>Adjudication</c> / <c>Frais</c> / <c>Autre</c>) ; <c>null</c> pour une validation.</summary>
    public string? Part { get; init; }

    /// <summary>Version de la table au moment de la modification (traçabilité F03 §5).</summary>
    public required string MappingVersion { get; init; }

    /// <summary>Valeur « avant » sérialisée en JSON ; <c>null</c> pour un ajout.</summary>
    public string? BeforeJson { get; init; }

    /// <summary>Valeur « après » sérialisée en JSON ; <c>null</c> pour une suppression.</summary>
    public string? AfterJson { get; init; }

    /// <summary>Identité Keycloak de l'opérateur auteur de la modification.</summary>
    public required Guid OperatorId { get; init; }

    /// <summary>Nom affiché de l'opérateur (aide à la lecture), facultatif.</summary>
    public string? OperatorName { get; init; }

    /// <summary>Horodatage de la modification (UTC).</summary>
    public required DateTimeOffset OccurredAt { get; init; }
}
