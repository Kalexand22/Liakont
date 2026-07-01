namespace Liakont.Modules.Ged.Domain.Catalog;

using System;
using System.Collections.Generic;

/// <summary>
/// Définition d'axe RÉSOLUE du catalogue GED (projection de <c>ged_catalog.axis_definitions</c> lue par
/// <c>IAxisCatalog</c>, F19 §3.3.1/§3.7). Elle porte tout ce dont le handler d'écriture (GED04) a besoin pour
/// ranger une valeur : le <see cref="DataType"/> technique (quelle colonne typée), l'échelle décimale
/// (<see cref="ValueScale"/>) d'un axe <c>number</c>, la cardinalité (<see cref="IsMultiValue"/> — un axe MONO
/// exige la garde de concurrence RL-02) et l'état d'activation (<see cref="IsActive"/> — un axe inactif est
/// refusé, jamais deviner, règle 2). Pour un axe <c>enum</c>, <see cref="AllowedEnumValues"/> porte le
/// vocabulaire déclaré (<c>ged_catalog.axis_values</c>) : une valeur hors vocabulaire est refusée (règle 2).
/// </summary>
public sealed record AxisDefinition
{
    /// <summary>Identité de l'axe (<c>ged_catalog.axis_definitions.id</c>).</summary>
    public required Guid Id { get; init; }

    /// <summary>Code machine stable de l'axe (paramétrage tenant, UNIQUE).</summary>
    public required string Code { get; init; }

    /// <summary>Système de types technique de l'axe : fixe la colonne de valeur typée et la normalisation.</summary>
    public required AxisDataType DataType { get; init; }

    /// <summary>Échelle décimale d'un axe <c>number</c> ([0..9]) ; <see langword="null"/> = valeur brute.</summary>
    public int? ValueScale { get; init; }

    /// <summary>Un axe multi-valeur accepte plusieurs valeurs courantes ; un axe MONO n'en garde qu'une (RL-02).</summary>
    public required bool IsMultiValue { get; init; }

    /// <summary>Un axe inactif ne reçoit aucune valeur (désactivation logique, jamais DELETE d'un axe utilisé).</summary>
    public required bool IsActive { get; init; }

    /// <summary>
    /// Vocabulaire déclaré (codes actifs de <c>ged_catalog.axis_values</c>) d'un axe <c>enum</c> ; vide pour tout
    /// autre type. Le handler refuse une valeur d'enum hors de cet ensemble (jamais deviner, règle 2).
    /// </summary>
    public IReadOnlyList<string> AllowedEnumValues { get; init; } = Array.Empty<string>();
}
