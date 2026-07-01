namespace Liakont.Modules.Ged.Application.Mapping;

using System;

/// <summary>
/// Entrée du journal APPEND-ONLY des profils de mapping GED (F19 §4.5, <c>ged_catalog.ged_mapping_change_log</c> ;
/// miroir de <c>MappingChangeLogEntry</c> du domaine TVA). Toute naissance / validation / mutation d'un profil est
/// tracée de façon immuable ; une correction se fait par une NOUVELLE entrée, jamais par UPDATE/DELETE (règle 4,
/// INV-GED-02). Le vocabulaire <see cref="ChangeType"/> reste un texte libre (comme <c>catalog_change_log</c>).
/// </summary>
public sealed record GedMappingChangeLogEntry
{
    /// <summary>Type de changement (texte libre, ex. « profile_created », « profile_validated »).</summary>
    public required string ChangeType { get; init; }

    /// <summary>Identifiant du profil concerné (soft-link, l'audit survit à toute désactivation).</summary>
    public required Guid ProfileId { get; init; }

    /// <summary>Type de document du profil (traçabilité).</summary>
    public required string DocumentType { get; init; }

    /// <summary>Version du profil au moment du changement (traçabilité).</summary>
    public required string ProfileVersion { get; init; }

    /// <summary>État avant (JSON), ou <see langword="null"/> pour une création.</summary>
    public string? BeforeJson { get; init; }

    /// <summary>État après (JSON), ou <see langword="null"/> pour une suppression logique.</summary>
    public string? AfterJson { get; init; }

    /// <summary>Identité de l'opérateur/valideur (Keycloak), ou <see langword="null"/>.</summary>
    public string? OperatorIdentity { get; init; }

    /// <summary>Nom d'affichage de l'opérateur (aide à la lecture), ou <see langword="null"/>.</summary>
    public string? OperatorName { get; init; }
}
