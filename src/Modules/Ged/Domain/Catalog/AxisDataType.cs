namespace Liakont.Modules.Ged.Domain.Catalog;

/// <summary>
/// Système de types FERMÉ d'un axe GED (F19 §3.3.1) — vocabulaire <b>technique</b> de la plateforme, miroir
/// exact de la contrainte SQL <c>ck_axis_def_data_type</c> (<c>ged_catalog.axis_definitions.data_type</c>).
/// Il fixe la colonne de valeur typée utilisée par un lien (<c>ged_index.document_axis_links</c>, §3.4.3) et
/// le mode de validation de <see cref="ValueNormalizer"/>.
/// <para>
/// À NE PAS confondre avec le vocabulaire <b>métier</b> (types d'entité <see cref="EntityType"/>, codes
/// d'axe, valeurs d'enum) qui, lui, est <b>polymorphe / paramétrable</b> et n'est JAMAIS un enum figé
/// (INV-GED-12, règle 7). Seul le système de types techniques est fermé. La correspondance vers le code SQL
/// canonique (<c>'string'</c>, <c>'number'</c>…) vit dans <see cref="AxisDataTypes"/>.
/// </para>
/// </summary>
public enum AxisDataType
{
    /// <summary>Chaîne libre — code SQL <c>'string'</c> (<c>value_string</c>).</summary>
    Text,

    /// <summary>Date calendaire ISO-8601 — code SQL <c>'date'</c> (<c>value_date</c>).</summary>
    Date,

    /// <summary>Nombre exact <c>decimal</c>, échelle portée par l'axe — code SQL <c>'number'</c> (<c>value_number</c>) — JAMAIS double/float.</summary>
    Number,

    /// <summary>Booléen — code SQL <c>'boolean'</c> (<c>value_boolean</c>).</summary>
    Boolean,

    /// <summary>Code d'un vocabulaire déclaré (<c>ged_catalog.axis_values</c>) — code SQL <c>'enum'</c>, rangé en <c>value_string</c>.</summary>
    Enum,

    /// <summary>Référence vers une instance d'entité du graphe — code SQL <c>'entity'</c> (<c>value_entity_id</c>).</summary>
    Entity,

    /// <summary>Fragment JSON de présentation — code SQL <c>'json'</c> (<c>value_json</c>).</summary>
    Json,
}
