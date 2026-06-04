namespace Liakont.Modules.Validation.Domain.Identity;

/// <summary>
/// Validation d'un SIRET (F04 §3.1 / §4.1) : 14 chiffres satisfaisant la clé de Luhn standard sur
/// les 14 chiffres. La dérogation La Poste documentée par F04 §4.1 porte sur le SIREN (356000000),
/// pas sur l'algorithme d'établissement du SIRET : aucune règle d'établissement n'est inventée ici
/// (CLAUDE.md n°2) — toute extension passerait par un amendement de F04.
/// </summary>
public static class SiretValidator
{
    /// <summary>Indique si <paramref name="siret"/> est un SIRET valide (14 chiffres + Luhn).</summary>
    /// <param name="siret">Le SIRET à contrôler (absent = <c>null</c>).</param>
    /// <returns><c>true</c> si le SIRET est valide, sinon <c>false</c>.</returns>
    public static bool IsValid(string? siret)
    {
        if (string.IsNullOrEmpty(siret) || siret.Length != 14)
        {
            return false;
        }

        return Luhn.IsValid(siret);
    }
}
