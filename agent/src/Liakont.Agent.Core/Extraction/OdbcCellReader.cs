namespace Liakont.Agent.Core.Extraction;

using System;
using System.Data;
using System.Globalization;

/// <summary>
/// Lecteurs de cellules GÉNÉRIQUES sur un <see cref="IDataReader"/>, partagés par les adaptateurs
/// source ODBC. Encapsulent <see cref="DBNull"/> et la conversion de type avec des erreurs TYPÉES
/// (F01-F02 R7) : une colonne absente du résultat ou une valeur de type inattendu lève une
/// <see cref="SourceSchemaException"/> (fatale, schéma incompatible). Aucune logique métier : pur
/// transport de données source en lecture seule. Les nombres sont lus en <see cref="CultureInfo.InvariantCulture"/>.
/// </summary>
public static class OdbcCellReader
{
    /// <summary>Lit une chaîne, ou <c>null</c> si la cellule est NULL.</summary>
    /// <param name="reader">Le lecteur positionné sur une ligne.</param>
    /// <param name="column">Le nom de la colonne.</param>
    /// <returns>La valeur, ou <c>null</c>.</returns>
    public static string? GetString(IDataReader reader, string column)
    {
        object value = Cell(reader, column);
        return value == DBNull.Value ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    /// <summary>Lit une chaîne OBLIGATOIRE (lève si NULL ou vide).</summary>
    /// <param name="reader">Le lecteur positionné sur une ligne.</param>
    /// <param name="column">Le nom de la colonne.</param>
    /// <returns>La valeur non vide.</returns>
    public static string GetRequiredString(IDataReader reader, string column)
    {
        string? value = GetString(reader, column);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new SourceSchemaException(
                $"Champ source obligatoire « {column} » absent ou vide : schéma incompatible. Vérifiez l'extraction des données source.");
        }

        return value!;
    }

    /// <summary>Lit un montant <c>decimal</c> OBLIGATOIRE (lève si NULL).</summary>
    /// <param name="reader">Le lecteur positionné sur une ligne.</param>
    /// <param name="column">Le nom de la colonne.</param>
    /// <returns>Le montant.</returns>
    public static decimal GetRequiredDecimal(IDataReader reader, string column)
    {
        object value = Cell(reader, column);
        if (value == DBNull.Value)
        {
            throw new SourceSchemaException(
                $"Montant source obligatoire « {column} » absent (NULL) : document bloqué, jamais deviné (ADR-0004 D3-3).");
        }

        return ToDecimal(value, column);
    }

    /// <summary>Lit un montant <c>decimal</c>, ou <c>null</c> si la cellule est NULL.</summary>
    /// <param name="reader">Le lecteur positionné sur une ligne.</param>
    /// <param name="column">Le nom de la colonne.</param>
    /// <returns>Le montant, ou <c>null</c>.</returns>
    public static decimal? GetNullableDecimal(IDataReader reader, string column)
    {
        object value = Cell(reader, column);
        return value == DBNull.Value ? (decimal?)null : ToDecimal(value, column);
    }

    /// <summary>Lit un nombre <c>double</c> (float legacy) OBLIGATOIRE (lève si NULL).</summary>
    /// <param name="reader">Le lecteur positionné sur une ligne.</param>
    /// <param name="column">Le nom de la colonne.</param>
    /// <returns>La valeur.</returns>
    public static double GetRequiredDouble(IDataReader reader, string column)
    {
        object value = Cell(reader, column);
        if (value == DBNull.Value)
        {
            throw new SourceSchemaException(
                $"Montant source obligatoire « {column} » absent (NULL) : document bloqué, jamais deviné (ADR-0004 D3-3).");
        }

        return ToDouble(value, column);
    }

    /// <summary>Lit un nombre <c>double</c> (float legacy), ou <c>null</c> si la cellule est NULL.</summary>
    /// <param name="reader">Le lecteur positionné sur une ligne.</param>
    /// <param name="column">Le nom de la colonne.</param>
    /// <returns>La valeur, ou <c>null</c>.</returns>
    public static double? GetNullableDouble(IDataReader reader, string column)
    {
        object value = Cell(reader, column);
        return value == DBNull.Value ? (double?)null : ToDouble(value, column);
    }

    /// <summary>Lit une date OBLIGATOIRE (lève si NULL ou illisible).</summary>
    /// <param name="reader">Le lecteur positionné sur une ligne.</param>
    /// <param name="column">Le nom de la colonne.</param>
    /// <returns>La date.</returns>
    public static DateTime GetRequiredDate(IDataReader reader, string column)
    {
        DateTime? value = GetNullableDate(reader, column);
        if (value is null)
        {
            throw new SourceSchemaException(
                $"Date source obligatoire « {column} » absente ou illisible : document bloqué, jamais devinée (ADR-0004 D3-3).");
        }

        return value.Value;
    }

    /// <summary>Lit une date, ou <c>null</c> si la cellule est NULL.</summary>
    /// <param name="reader">Le lecteur positionné sur une ligne.</param>
    /// <param name="column">Le nom de la colonne.</param>
    /// <returns>La date, ou <c>null</c>.</returns>
    public static DateTime? GetNullableDate(IDataReader reader, string column)
    {
        object value = Cell(reader, column);
        if (value == DBNull.Value)
        {
            return null;
        }

        try
        {
            return Convert.ToDateTime(value, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException || ex is InvalidCastException)
        {
            throw new SourceSchemaException(
                $"Date source illisible pour le champ « {column} » : schéma incompatible. Vérifiez l'extraction des données source.",
                ex);
        }
    }

    /// <summary>Lit un booléen (<c>false</c> si NULL).</summary>
    /// <param name="reader">Le lecteur positionné sur une ligne.</param>
    /// <param name="column">Le nom de la colonne.</param>
    /// <returns>La valeur, ou <c>false</c> si NULL.</returns>
    public static bool GetBool(IDataReader reader, string column)
    {
        object value = Cell(reader, column);
        if (value == DBNull.Value)
        {
            return false;
        }

        try
        {
            return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException || ex is InvalidCastException)
        {
            throw new SourceSchemaException(
                $"Booléen source illisible pour le champ « {column} » : schéma incompatible. Vérifiez l'extraction des données source.",
                ex);
        }
    }

    /// <summary>Lit un entier (0 si NULL). Utile pour les agrégats <c>COUNT</c>.</summary>
    /// <param name="reader">Le lecteur positionné sur une ligne.</param>
    /// <param name="column">Le nom de la colonne.</param>
    /// <returns>La valeur, ou 0 si NULL.</returns>
    public static int GetInt(IDataReader reader, string column)
    {
        object value = Cell(reader, column);
        if (value == DBNull.Value)
        {
            return 0;
        }

        try
        {
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException || ex is InvalidCastException || ex is OverflowException)
        {
            throw new SourceSchemaException(
                $"Entier source illisible pour le champ « {column} » : schéma incompatible. Vérifiez l'extraction.",
                ex);
        }
    }

    private static decimal ToDecimal(object value, string column)
    {
        try
        {
            return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException || ex is InvalidCastException || ex is OverflowException)
        {
            throw new SourceSchemaException(
                $"Valeur source illisible pour le champ « {column} » (montant attendu) : schéma incompatible. Vérifiez l'extraction.",
                ex);
        }
    }

    private static double ToDouble(object value, string column)
    {
        try
        {
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException || ex is InvalidCastException || ex is OverflowException)
        {
            throw new SourceSchemaException(
                $"Valeur source illisible pour le champ « {column} » (nombre attendu) : schéma incompatible. Vérifiez l'extraction.",
                ex);
        }
    }

    private static object Cell(IDataReader reader, string column)
    {
        if (reader is null)
        {
            throw new ArgumentNullException(nameof(reader));
        }

        try
        {
            return reader[column];
        }
        catch (IndexOutOfRangeException ex)
        {
            throw new SourceSchemaException(
                $"Colonne source attendue « {column} » absente du résultat : schéma incompatible. Vérifiez la requête d'extraction.",
                ex);
        }
    }
}
