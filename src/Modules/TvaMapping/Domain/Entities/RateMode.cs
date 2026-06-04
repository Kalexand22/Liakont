namespace Liakont.Modules.TvaMapping.Domain.Entities;

/// <summary>
/// Mode de détermination du taux d'une règle de mapping (item TVA01 §2, F03 §4.1).
/// </summary>
public enum RateMode
{
    /// <summary>Taux fixe figé dans la règle (ex. 20 % pour le taux normal).</summary>
    Fixed = 0,

    /// <summary>
    /// Taux calculé à partir du document source (F03 §4.1 : le taux des frais peut être
    /// <c>montant_tva_frais / montant_frais_ht</c> plutôt que figé). La valeur n'est pas connue
    /// dans la table ; elle est résolue par le moteur de mapping (TVA02) à l'exécution.
    /// </summary>
    ComputedFromSource = 1,
}
