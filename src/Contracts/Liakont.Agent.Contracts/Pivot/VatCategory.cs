namespace Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Catégorie de TVA (code UNCL5305 — EN 16931 BT-151). C'est le RÉSULTAT du mapping TVA, réalisé
/// sur la PLATEFORME (module TVA, lot F03) à partir du régime source brut
/// (<see cref="PivotLineDto.SourceRegimeCodes"/>). L'agent ne renseigne PAS cette valeur : elle
/// est nullable côté contrat et reste nulle tant que le mapping plateforme n'a pas tranché
/// (note v6 PIV01 : la trace de mapping n'est pas dans le contrat). La liste S/AA/AAA/Z/E/AE/G/K/O
/// est celle de F03-Mapping-TVA.md §2.1 (catégories UNCL5305 acceptées par la PA, cohérentes
/// staging) — aucune n'est inventée (CLAUDE.md n°2). ⚠️ AA (taux réduit) et AAA (super réduit) ne
/// figurent pas dans toutes les listes EN 16931 strictes (BT-151) ; ils sont acceptés par
/// B2Brouter avec ses taux FR préchargés, mais restent À CONFIRMER sur le profil EXTENDED-CTC-FR
/// (F03 §2.1 + décision 4) avant figeage du mapping de production.
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
