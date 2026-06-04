namespace Liakont.Modules.Validation.Domain.Rules;

using System.Globalization;

/// <summary>
/// Formatage des montants et dates pour les messages opérateur (français, F04 §5). Centralisé pour
/// garantir un format cohérent et un fournisseur de format explicite (analyseur CA1305).
/// </summary>
internal static class RuleMessageFormat
{
    private static readonly CultureInfo FrCulture = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Formate un montant en euros, format français (ex. « 1 162,80 € »).</summary>
    /// <param name="value">Le montant à formater.</param>
    /// <returns>Le montant formaté suivi du symbole euro.</returns>
    public static string FormatEuro(decimal value) => value.ToString("N2", FrCulture) + " €";

    /// <summary>Formate une date au format français court (jj/MM/aaaa).</summary>
    /// <param name="value">La date à formater.</param>
    /// <returns>La date formatée.</returns>
    public static string FormatDate(DateTime value) => value.ToString("dd/MM/yyyy", FrCulture);

    /// <summary>
    /// Formate un montant en culture invariante, pour les détails techniques (journal/audit) où un
    /// format stable et indépendant de la culture est requis (fournisseur de format explicite, CA1305).
    /// </summary>
    /// <param name="value">Le montant à formater.</param>
    /// <returns>Le montant en représentation invariante.</returns>
    public static string FormatInvariant(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}
