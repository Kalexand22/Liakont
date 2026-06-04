namespace Liakont.Modules.TvaMapping.Application;

using Liakont.Modules.TvaMapping.Domain.Entities;

/// <summary>
/// Entrée du journal append-only des modifications de la table de mapping TVA (item TVA05 §3). Écrite
/// EN BASE dans la MÊME transaction que la mutation qu'elle décrit (atomicité, item TVA05 §5) : jamais
/// la mutation sans son entrée, jamais l'inverse. Immuable côté base (aucun chemin d'update/delete —
/// même discipline que <c>DocumentEvent</c>, CLAUDE.md n°4). Les valeurs avant/après sont sérialisées
/// en JSON par l'infrastructure (<c>MappingChangeLogFactory</c>).
/// </summary>
/// <remarks>
/// Traçabilité document→règle : l'édition est EN PLACE (item TVA05 §1) — <c>mapping_version</c>
/// n'est PAS auto-incrémenté à chaque mutation. La preuve fiscale « quelle règle a produit quel motif
/// d'exonération » est figée à l'émission par la <c>MappingTrace</c> (F03 §4.2, item TVA02), et le
/// présent journal horodaté (<c>occurred_at</c> + avant/après + auteur) reconstitue l'état de la table
/// à toute date. Le versionnage humain de la table (cmp-v1 → cmp-v2, F03 décision #6.5, ❓ non tranchée)
/// est un workflow expert-comptable hors périmètre TVA05 : aucun schéma d'incrément n'est inventé ici
/// (CLAUDE.md n°2).
/// </remarks>
public sealed record MappingChangeLogEntry
{
    /// <summary>Tenant propriétaire (isolation par société — CLAUDE.md n°9).</summary>
    public required Guid CompanyId { get; init; }

    /// <summary>Table de mapping concernée.</summary>
    public required Guid TableId { get; init; }

    /// <summary>Version de la table au moment de la modification (traçabilité F03 §5).</summary>
    public required string MappingVersion { get; init; }

    /// <summary>Nature de la modification.</summary>
    public required MappingChangeType ChangeType { get; init; }

    /// <summary>Code régime de la règle concernée ; <c>null</c> pour une validation de table.</summary>
    public string? SourceRegimeCode { get; init; }

    /// <summary>Part de la règle concernée ; <c>null</c> pour une validation de table.</summary>
    public MappingPart? Part { get; init; }

    /// <summary>Valeur « avant » sérialisée en JSON ; <c>null</c> pour un ajout.</summary>
    public string? BeforeJson { get; init; }

    /// <summary>Valeur « après » sérialisée en JSON ; <c>null</c> pour une suppression.</summary>
    public string? AfterJson { get; init; }

    /// <summary>Identité Keycloak de l'opérateur auteur de la modification (item TVA05 §3).</summary>
    public required Guid OperatorId { get; init; }

    /// <summary>Nom affiché de l'opérateur (aide à la lecture du journal), facultatif.</summary>
    public string? OperatorName { get; init; }
}
