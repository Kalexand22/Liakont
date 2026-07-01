namespace Liakont.Modules.Ged.Domain.Catalog;

using System;

/// <summary>
/// Type d'entité GED <b>polymorphe</b> (F19 §3.3.2) — modèle Domain d'une ligne <c>ged_catalog.entity_types</c>.
/// <para>
/// « Polymorphe » = le type d'entité est porté par un <see cref="Code"/> libre (paramétrage tenant), JAMAIS
/// par un enum C# figé : ajouter un type d'entité métier est de la CONFIG (seeds fictifs en
/// <c>deployments/</c>), pas du code (INV-GED-12, règle 7). Ce registre ne remplace ni n'absorbe les entités fiscales/socle existantes
/// (Mandats, Stratum.Modules.Party) — une entité GED qui leur correspond s'y RÉFÈRE par soft-link (frontière
/// F19 §3.3.2, P1).
/// </para>
/// Value object immuable ; consommé à partir de GED04 (résolution d'axe <c>data_type='entity'</c>) et GED03c
/// (instances du graphe).
/// </summary>
public sealed record EntityType
{
    /// <summary>
    /// Crée un type d'entité GED. Refuse un <paramref name="code"/> ou un <paramref name="label"/> vide
    /// (jamais deviner, règle 2) ; une <paramref name="identityKey"/> vide est normalisée en
    /// <see langword="null"/> (absence de clé de dédup).
    /// </summary>
    public EntityType(
        string code,
        string label,
        string? identityKey = null,
        bool isConfidential = false,
        bool isActive = true)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Le code d'un type d'entité GED est obligatoire.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            throw new ArgumentException("Le libellé d'un type d'entité GED est obligatoire.", nameof(label));
        }

        Code = code;
        Label = label;
        IdentityKey = string.IsNullOrWhiteSpace(identityKey) ? null : identityKey;
        IsConfidential = isConfidential;
        IsActive = isActive;
    }

    /// <summary>Clé machine stable et libre du type d'entité (contrainte SQL <c>uq_entity_types_code</c>).</summary>
    public string Code { get; }

    /// <summary>Libellé opérateur (FR).</summary>
    public string Label { get; }

    /// <summary>
    /// Clé de résolution d'identité pour la déduplication (§4.4), ex. <c>"siret"</c> ; <see langword="null"/>
    /// = pas de déduplication automatique.
    /// </summary>
    public string? IdentityKey { get; }

    /// <summary>Entité confidentielle : non traversable/affichable sans le droit dédié (§6.5, INV-GED-10).</summary>
    public bool IsConfidential { get; }

    /// <summary>Désactivation logique (un type utilisé ne se supprime jamais — <c>is_active=false</c>).</summary>
    public bool IsActive { get; }
}
