namespace Liakont.Modules.Ged.Domain.Catalog;

using System;

/// <summary>
/// Conversion entre <see cref="AxisDataType"/> et son <b>code SQL canonique</b> (la valeur textuelle stockée
/// dans <c>ged_catalog.axis_definitions.data_type</c> et contrôlée par <c>ck_axis_def_data_type</c>). La
/// correspondance vit ici, en un seul endroit, pour rester alignée avec la contrainte SQL (le mapping
/// ligne → Domain est consommé à partir de GED04, <c>IAxisCatalog</c>).
/// </summary>
public static class AxisDataTypes
{
    /// <summary>Code SQL canonique (minuscule) d'un <see cref="AxisDataType"/>, tel que stocké en base.</summary>
    public static string ToSqlCode(this AxisDataType dataType) => dataType switch
    {
        AxisDataType.Text => "string",
        AxisDataType.Date => "date",
        AxisDataType.Number => "number",
        AxisDataType.Boolean => "boolean",
        AxisDataType.Enum => "enum",
        AxisDataType.Entity => "entity",
        AxisDataType.Json => "json",
        _ => throw new ArgumentOutOfRangeException(nameof(dataType), dataType, "Type d'axe GED inconnu."),
    };

    /// <summary>
    /// Analyse un code SQL de <c>data_type</c> vers son <see cref="AxisDataType"/>. Refuse tout code hors du
    /// vocabulaire fermé (jamais deviner, règle 2) — symétrique de la contrainte <c>ck_axis_def_data_type</c>.
    /// </summary>
    public static AxisDataType Parse(string sqlCode)
    {
        ArgumentNullException.ThrowIfNull(sqlCode);
        return sqlCode switch
        {
            "string" => AxisDataType.Text,
            "date" => AxisDataType.Date,
            "number" => AxisDataType.Number,
            "boolean" => AxisDataType.Boolean,
            "enum" => AxisDataType.Enum,
            "entity" => AxisDataType.Entity,
            "json" => AxisDataType.Json,
            _ => throw new ArgumentOutOfRangeException(
                nameof(sqlCode),
                sqlCode,
                "Code de data_type d'axe GED hors du vocabulaire technique fermé (string|date|number|boolean|enum|entity|json)."),
        };
    }
}
