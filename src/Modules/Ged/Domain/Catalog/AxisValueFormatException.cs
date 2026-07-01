namespace Liakont.Modules.Ged.Domain.Catalog;

using System;

/// <summary>
/// Levée quand une valeur d'axe BRUTE ne correspond pas au <see cref="AxisDataType"/> déclaré de l'axe
/// (F19 §3.7, règle 2 : <b>refus, jamais deviner</b>). Le handler d'écriture (GED04) enrichit le message
/// opérateur du numéro de document et de l'action corrective (règle 12) ; à ce niveau Domain le message
/// reste centré sur la valeur et le type attendu.
/// </summary>
public sealed class AxisValueFormatException : Exception
{
    public AxisValueFormatException(AxisDataType dataType, string rawValue, string reason)
        : base($"Valeur d'axe GED invalide pour le type « {dataType.ToSqlCode()} » : {reason} "
            + "Corrigez la valeur source ou le type de l'axe (jamais deviner, CLAUDE.md n.2).")
    {
        DataType = dataType;
        RawValue = rawValue;
    }

    /// <summary>Type d'axe déclaré contre lequel la valeur a échoué.</summary>
    public AxisDataType DataType { get; }

    /// <summary>La valeur brute rejetée (jamais interprétée « au mieux »).</summary>
    public string RawValue { get; }
}
