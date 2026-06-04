namespace Liakont.Modules.TvaMapping.Domain.Entities;

/// <summary>
/// Part de la ligne à laquelle une règle de mapping s'applique (F03 §4.1, item TVA01 §2).
/// Le modèle du régime de la marge (art. 297 A CGI) distingue l'adjudication (souvent exonérée,
/// catégorie E + VATEX) des frais (toujours taxables) — F03 §2.3. <c>Autre</c> couvre les lignes
/// hors de ce découpage. Énumération issue de la spec ; aucune valeur n'est inventée (CLAUDE.md n°2).
/// </summary>
public enum MappingPart
{
    /// <summary>Adjudication (le bien vendu).</summary>
    Adjudication = 0,

    /// <summary>Frais (frais acheteur / frais de service), toujours taxables (F03 §2.3).</summary>
    Frais = 1,

    /// <summary>Autre part, hors du découpage adjudication/frais.</summary>
    Autre = 2,
}
