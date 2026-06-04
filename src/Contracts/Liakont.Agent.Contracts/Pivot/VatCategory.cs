namespace Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Catégorie de TVA (code UNCL5305 — EN 16931 BT-151, F01-F02 §3.4). C'est le RÉSULTAT du
/// mapping TVA, réalisé sur la PLATEFORME (module TVA, lot F03) à partir du régime source brut
/// (<see cref="PivotLineDto.SourceRegimeCodes"/>). L'agent ne renseigne PAS cette valeur : elle
/// est nullable côté contrat et reste nulle tant que le mapping plateforme n'a pas tranché
/// (note v6 PIV01 : la trace de mapping n'est pas dans le contrat). Les valeurs proviennent
/// exclusivement du référentiel UNCL5305 — aucune catégorie n'est inventée (CLAUDE.md n°2).
/// </summary>
public enum VatCategory
{
    /// <summary>S — taux standard.</summary>
    S = 1,

    /// <summary>AA — taux réduit.</summary>
    AA = 2,

    /// <summary>AAA — taux particulier (super-réduit).</summary>
    AAA = 3,

    /// <summary>Z — taux zéro.</summary>
    Z = 4,

    /// <summary>E — exonéré (motif VATEX requis).</summary>
    E = 5,

    /// <summary>AE — autoliquidation (reverse charge).</summary>
    AE = 6,

    /// <summary>G — export hors UE détaxé.</summary>
    G = 7,

    /// <summary>K — livraison/prestation intracommunautaire.</summary>
    K = 8,

    /// <summary>O — hors champ d'application de la TVA.</summary>
    O = 9,
}
