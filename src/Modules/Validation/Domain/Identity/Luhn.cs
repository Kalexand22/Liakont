namespace Liakont.Modules.Validation.Domain.Identity;

/// <summary>
/// Algorithme de Luhn (F04 §4.1) : en partant de la droite, un chiffre sur deux est doublé (et
/// réduit de 9 s'il dépasse 9) ; la chaîne est valide si la somme pondérée est un multiple de 10.
/// Utilisé par <see cref="SirenValidator"/> et <see cref="SiretValidator"/>. L'appelant garantit la
/// longueur attendue : une chaîne vide donnerait une somme de 0 (multiple de 10).
/// </summary>
internal static class Luhn
{
    /// <summary>
    /// Indique si <paramref name="digits"/> (chaîne supposée numérique) satisfait la clé de Luhn.
    /// Retourne <c>false</c> dès qu'un caractère non numérique est rencontré.
    /// </summary>
    /// <param name="digits">La chaîne de chiffres à contrôler (longueur déjà validée par l'appelant).</param>
    /// <returns><c>true</c> si la somme de Luhn est un multiple de 10, sinon <c>false</c>.</returns>
    internal static bool IsValid(string digits)
    {
        var sum = 0;
        var doubleDigit = false;

        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var c = digits[i];
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
