namespace Liakont.Modules.TenantSettings.Domain.Services;

/// <summary>
/// Validation d'un SIREN : 9 chiffres + clé de Luhn (F12-A §2, blueprint §7.1).
/// </summary>
/// <remarks>
/// Duplication TEMPORAIRE et documentée de la règle de validation SIREN qui sera portée par
/// VAL02 (cf. F12-A §2 et description CFG02). À consolider derrière la règle VAL02 dès que
/// le lot VAL est livré — ne pas diverger entre les deux implémentations entre-temps.
/// </remarks>
public static class SirenValidator
{
    /// <summary>
    /// Indique si <paramref name="siren"/> est composé de 9 chiffres et satisfait la clé de Luhn.
    /// </summary>
    public static bool IsValid(string? siren)
    {
        if (string.IsNullOrEmpty(siren) || siren.Length != 9)
        {
            return false;
        }

        var sum = 0;
        var doubleDigit = false;

        // Luhn : on parcourt de droite à gauche, on double un chiffre sur deux (le 2e en partant
        // de la droite), et on retranche 9 si le résultat dépasse 9. Total multiple de 10 = valide.
        for (var i = siren.Length - 1; i >= 0; i--)
        {
            var c = siren[i];
            if (c < '0' || c > '9')
            {
                return false;
            }

            var digit = c - '0';
            if (doubleDigit)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
            doubleDigit = !doubleDigit;
        }

        return sum % 10 == 0;
    }
}
