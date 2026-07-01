namespace Liakont.Modules.Ged.Domain.Catalog;

using System;

/// <summary>
/// Définition RÉSOLUE d'un type d'entité GED (projection de <c>ged_catalog.entity_types</c> lue par
/// <c>IEntityCatalog</c>, F19 §3.3.2/§4.4). Elle porte ce dont le consommateur d'ingestion (GED05b) a besoin pour
/// résoudre une entité déclarée : l'identité du type (<see cref="Id"/>), sa clé de résolution d'identité éventuelle
/// (<see cref="IdentityKey"/> — <see langword="null"/> = pas de déduplication automatique, création par observation,
/// §4.4), son état d'activation (<see cref="IsActive"/> — un type inactif est refusé, jamais deviner, règle 2) et sa
/// confidentialité (<see cref="IsConfidential"/>, §6.5). Le <see cref="Code"/> est un libellé machine libre
/// (paramétrage tenant), jamais un enum figé côté plateforme (règle 7).
/// </summary>
public sealed record EntityTypeDefinition
{
    /// <summary>Identité du type d'entité (<c>ged_catalog.entity_types.id</c>).</summary>
    public required Guid Id { get; init; }

    /// <summary>Code machine stable du type (paramétrage tenant, UNIQUE).</summary>
    public required string Code { get; init; }

    /// <summary>
    /// Clé de résolution d'identité déclarée (ex. <c>siret</c>) ; <see langword="null"/> = pas de déduplication
    /// automatique (création par observation, §4.4).
    /// </summary>
    public string? IdentityKey { get; init; }

    /// <summary>Type confidentiel : entité/relation non traversable/affichable sans le droit dédié (§6.5).</summary>
    public required bool IsConfidential { get; init; }

    /// <summary>Un type inactif ne reçoit aucune instance (désactivation logique, jamais DELETE d'un type utilisé).</summary>
    public required bool IsActive { get; init; }
}
