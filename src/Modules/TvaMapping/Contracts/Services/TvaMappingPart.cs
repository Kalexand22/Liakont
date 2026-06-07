namespace Liakont.Modules.TvaMapping.Contracts.Services;

/// <summary>
/// Part de la ligne à laquelle une règle de mapping s'applique, à la frontière Contracts (F03 §4.1) :
/// adjudication / frais / autre. Énumération issue de la spec ; aucune valeur n'est inventée (CLAUDE.md n°2).
/// La DÉRIVATION de la part depuis une ligne pivot est une décision fiscale OUVERTE (aucune règle sourcée —
/// ADR-0004 / F03 §2.3) : l'appelant (pipeline, PIP01b) FOURNIT la part ; le service ne la devine jamais.
/// </summary>
public enum TvaMappingPart
{
    /// <summary>Adjudication (le bien vendu).</summary>
    Adjudication = 0,

    /// <summary>Frais (frais acheteur / frais de service), toujours taxables (F03 §2.3).</summary>
    Frais = 1,

    /// <summary>Autre part, hors du découpage adjudication/frais.</summary>
    Autre = 2,
}
