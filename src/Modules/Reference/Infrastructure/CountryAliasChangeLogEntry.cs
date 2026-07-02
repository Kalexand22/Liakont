namespace Liakont.Modules.Reference.Infrastructure;

/// <summary>
/// Entrée du journal append-only des mutations du référentiel de correspondance pays (ADR-0038, §5). Écrite
/// EN BASE dans la MÊME transaction que l'upsert / la suppression qu'elle décrit (atomicité). Immuable côté
/// base : des triggers rejettent tout UPDATE/DELETE d'une entrée existante et tout TRUNCATE (CLAUDE.md n°4).
/// Les valeurs avant/après sont sérialisées en JSON par <see cref="CountryAliasChangeLogFactory"/>.
/// </summary>
internal sealed record CountryAliasChangeLogEntry
{
    /// <summary>Identifiant unique de l'entrée de journal.</summary>
    public required Guid Id { get; init; }

    /// <summary>Code source normalisé (clé de la correspondance).</summary>
    public required string SourceCode { get; init; }

    /// <summary>Nature de la modification.</summary>
    public required CountryAliasChangeType ChangeType { get; init; }

    /// <summary>Valeur « avant » sérialisée en JSON ; <c>null</c> pour un ajout.</summary>
    public string? BeforeJson { get; init; }

    /// <summary>Valeur « après » sérialisée en JSON ; <c>null</c> pour une suppression.</summary>
    public string? AfterJson { get; init; }

    /// <summary>Identité Keycloak de l'opérateur auteur de la mutation.</summary>
    public required Guid OperatorId { get; init; }

    /// <summary>Nom affiché de l'opérateur (aide à la lecture du journal), facultatif.</summary>
    public string? OperatorName { get; init; }

    /// <summary>Horodatage UTC de la mutation.</summary>
    public required DateTimeOffset OccurredAt { get; init; }
}
