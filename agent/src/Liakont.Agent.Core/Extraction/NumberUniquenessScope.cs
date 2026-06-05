namespace Liakont.Agent.Core.Extraction;

/// <summary>
/// Granularité d'unicité du numéro de document dans la source (capacité déclarée — ADR-0004 D2).
/// Renseigne la clé d'idempotence composite quand le numéro n'est pas unique au global
/// (multi-établissement, multi-série, remise à zéro annuelle). Déclaratif uniquement.
/// </summary>
public enum NumberUniquenessScope
{
    /// <summary>Le numéro est unique pour tout l'émetteur.</summary>
    Global = 1,

    /// <summary>Le numéro n'est unique que par établissement / point de vente.</summary>
    PerEstablishment = 2,

    /// <summary>Le numéro n'est unique que par série de numérotation.</summary>
    PerSeries = 3,

    /// <summary>Le numéro n'est unique que par année (remise à zéro annuelle).</summary>
    PerYear = 4,
}
