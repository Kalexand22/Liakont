namespace Liakont.Modules.TvaMapping.Domain.Services;

using Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Convertit un code catégorie UNCL5305 (chaîne) en <see cref="VatCategory"/>. Toute valeur hors de
/// la liste sourcée F03 §2.1 (S, AA, AAA, Z, E, AE, G, K, O) est REJETÉE — jamais devinée
/// (CLAUDE.md n°2). Utilisé par l'import de seed (TVA04) et l'édition console (TVA05) pour valider
/// la catégorie à l'écriture.
/// </summary>
public static class VatCategoryParser
{
    /// <summary>
    /// Liste des codes catégorie admis (F03 §2.1), DÉRIVÉE de l'enum <see cref="VatCategory"/> :
    /// source unique de vérité partagée avec <c>MappingTableValidator</c> (qui valide via
    /// <c>Enum.IsDefined</c>). Les deux chemins d'écriture ne peuvent plus diverger.
    /// </summary>
    public static readonly IReadOnlyList<string> AllowedCodes = Enum.GetNames<VatCategory>();

    /// <summary>
    /// Parse un code catégorie. Lève <see cref="ArgumentException"/> (message opérateur français)
    /// si le code est vide ou hors de la liste UNCL5305 admise.
    /// </summary>
    /// <param name="code">Code catégorie (ex. <c>"E"</c>), insensible aux espaces de bord.</param>
    public static VatCategory Parse(string? code)
    {
        var trimmed = code?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ArgumentException(
                "Code catégorie de TVA obligatoire. Catégories admises : " +
                string.Join(", ", AllowedCodes) + ".",
                nameof(code));
        }

        // Correspondance EXACTE avec un code admis (le nom de l'enum EST le code UNCL5305).
        // On n'utilise PAS Enum.TryParse seul : il accepterait une valeur numérique (« 5 » → E)
        // ou une casse libre, ce qui reviendrait à accepter une catégorie hors liste.
        if (AllowedCodes.Contains(trimmed, StringComparer.Ordinal))
        {
            return Enum.Parse<VatCategory>(trimmed, ignoreCase: false);
        }

        throw new ArgumentException(
            $"Catégorie de TVA inconnue : « {trimmed} ». Catégories admises : " +
            string.Join(", ", AllowedCodes) + " — aucune n'est devinée.",
            nameof(code));
    }
}
