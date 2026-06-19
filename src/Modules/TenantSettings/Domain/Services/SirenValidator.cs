namespace Liakont.Modules.TenantSettings.Domain.Services;

/// <summary>
/// Validation du SIREN du PROFIL TENANT : 9 chiffres. La clé de Luhn n'est PAS imposée (le SIREN
/// émetteur est un paramétrage de CONFIANCE ; cf. corps de <see cref="IsValid"/>). À NE PAS confondre
/// avec <c>Liakont.Modules.Validation.Domain.Identity.SirenValidator</c> (Luhn EXIGÉ sur les SIREN extraits).
/// </summary>
/// <remarks>
/// Décision de recette (Karl, 18/06/2026) : autoriser les SIREN de TEST des sandboxes PA (ex. SuperPDP
/// « Burger Queen »). Le SIREN émetteur est saisi par le déployeur/l'opérateur, pas extrait — on ne
/// re-contrôle donc pas sa clé de Luhn. Divergence ASSUMÉE (sur la seule clé de Luhn) avec la règle VAL02
/// (F12-A §2), qui reste à consolider.
/// </remarks>
public static class SirenValidator
{
    /// <summary>
    /// Indique si <paramref name="siren"/> est composé de 9 chiffres. La clé de Luhn n'est PAS vérifiée :
    /// le SIREN du profil tenant est un paramétrage de confiance (autorise les SIREN de test sandbox).
    /// </summary>
    public static bool IsValid(string? siren)
    {
        if (string.IsNullOrEmpty(siren) || siren.Length != 9)
        {
            return false;
        }

        foreach (var c in siren)
        {
            if (c < '0' || c > '9')
            {
                return false;
            }
        }

        return true;
    }
}
