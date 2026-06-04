namespace Liakont.Modules.Validation.Domain.Identity;

/// <summary>
/// Validation d'un SIREN (F04 §3.1 / §4.1) : 9 chiffres satisfaisant la clé de Luhn.
/// Dérogation documentée : le SIREN de La Poste (<see cref="LaPosteSiren"/>) est explicitement
/// autorisé (F04 §4.1) — aucune règle n'est inventée, la valeur vient de la spec.
/// </summary>
/// <remarks>
/// Validateur élémentaire du lot VAL (item VAL02). Il remplace à terme la copie temporaire portée
/// par <c>TenantSettings.Domain.Services.SirenValidator</c> (CFG02) ; les deux implémentations ne
/// doivent pas diverger d'ici la consolidation.
/// </remarks>
public static class SirenValidator
{
    /// <summary>
    /// SIREN de La Poste, autorisé par dérogation documentée (F04 §4.1). Constante exposée pour que
    /// le paramétrage et les tests référencent la même valeur de source.
    /// </summary>
    public const string LaPosteSiren = "356000000";

    /// <summary>Indique si <paramref name="siren"/> est un SIREN valide (9 chiffres + Luhn, ou La Poste).</summary>
    /// <param name="siren">Le SIREN à contrôler (absent = <c>null</c>).</param>
    /// <returns><c>true</c> si le SIREN est valide, sinon <c>false</c>.</returns>
    public static bool IsValid(string? siren)
    {
        if (string.IsNullOrEmpty(siren) || siren.Length != 9)
        {
            return false;
        }

        if (siren == LaPosteSiren)
        {
            return true;
        }

        return Luhn.IsValid(siren);
    }
}
