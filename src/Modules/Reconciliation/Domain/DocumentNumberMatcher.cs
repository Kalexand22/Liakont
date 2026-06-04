namespace Liakont.Modules.Reconciliation.Domain;

using System;

/// <summary>
/// Détecte la présence d'un NUMÉRO DE DOCUMENT dans un texte (nom de fichier ou contenu PDF) — stratégies
/// 1 et 2 du rapprochement (item TRK07, F06 §7 §1). La correspondance est délimitée : les caractères qui
/// bordent immédiatement le numéro trouvé ne doivent pas être alphanumériques, afin que « FAC-2026-0042 »
/// ne soit pas reconnu à tort à l'intérieur de « FAC-2026-00421 ». Comparaison insensible à la casse
/// (ordinale) — un numéro de document n'a pas de sémantique de casse.
/// </summary>
public static class DocumentNumberMatcher
{
    /// <summary>
    /// Indique si <paramref name="documentNumber"/> apparaît comme un jeton délimité dans
    /// <paramref name="haystack"/>. Renvoie <c>false</c> pour un numéro vide ou un texte nul/vide.
    /// </summary>
    public static bool Contains(string? haystack, string documentNumber)
    {
        if (string.IsNullOrWhiteSpace(haystack) || string.IsNullOrWhiteSpace(documentNumber))
        {
            return false;
        }

        string needle = documentNumber.Trim();
        int from = 0;
        while (from <= haystack.Length - needle.Length)
        {
            int index = haystack.IndexOf(needle, from, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return false;
            }

            char? before = index > 0 ? haystack[index - 1] : null;
            int afterIndex = index + needle.Length;
            char? after = afterIndex < haystack.Length ? haystack[afterIndex] : null;

            if (!IsAlphanumeric(before) && !IsAlphanumeric(after))
            {
                return true;
            }

            from = index + 1;
        }

        return false;
    }

    private static bool IsAlphanumeric(char? c) => c is { } value && char.IsLetterOrDigit(value);
}
