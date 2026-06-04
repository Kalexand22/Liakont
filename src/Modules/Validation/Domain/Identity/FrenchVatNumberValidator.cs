namespace Liakont.Modules.Validation.Domain.Identity;

using System.Globalization;

/// <summary>
/// Validation d'un numéro de TVA intracommunautaire français (F04 §4.2) : « FR » + clé de contrôle
/// (2 chiffres) + SIREN (9 chiffres), soit 13 caractères sans espace. La clé attendue vaut
/// <c>(12 + 3 × (SIREN mod 97)) mod 97</c>. Le validateur contrôle le format et la clé : la formule
/// vient de la spec (F04 §4.2), rien n'est inventé.
/// </summary>
public static class FrenchVatNumberValidator
{
    /// <summary>
    /// Indique si <paramref name="vatNumber"/> est un n° de TVA intracommunautaire français valide
    /// (format « FR » + clé + SIREN et clé cohérente avec le SIREN intégré).
    /// </summary>
    /// <param name="vatNumber">Le numéro de TVA à contrôler, sans espace (absent = <c>null</c>).</param>
    /// <returns><c>true</c> si le format et la clé sont valides, sinon <c>false</c>.</returns>
    public static bool IsValid(string? vatNumber)
    {
        // « FR » (2) + clé (2 chiffres) + SIREN (9 chiffres) = 13 caractères exactement.
        if (string.IsNullOrEmpty(vatNumber) || vatNumber.Length != 13)
        {
            return false;
        }

        if (vatNumber[0] != 'F' || vatNumber[1] != 'R')
        {
            return false;
        }

        var keyPart = vatNumber.Substring(2, 2);
        var sirenPart = vatNumber.Substring(4, 9);
        if (!IsAllAsciiDigits(keyPart) || !IsAllAsciiDigits(sirenPart))
        {
            return false;
        }

        var siren = int.Parse(sirenPart, NumberStyles.None, CultureInfo.InvariantCulture);
        var key = int.Parse(keyPart, NumberStyles.None, CultureInfo.InvariantCulture);
        var expectedKey = (12 + (3 * (siren % 97))) % 97;
        return key == expectedKey;
    }

    private static bool IsAllAsciiDigits(string value)
    {
        foreach (var c in value)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
