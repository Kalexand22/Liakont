namespace Liakont.Modules.Validation.Contracts.Classification;

/// <summary>
/// Résultat de la classification du type de document SOURCE brut
/// (<see cref="Liakont.Agent.Contracts.Pivot.PivotDocumentDto.SourceDocumentKind"/>) vers le type
/// canonique facture/avoir (F04 §3.5bis, ADR-0004 D3-3). La correspondance est du PARAMÉTRAGE de tenant
/// (validée par l'expert-comptable, propre à chaque logiciel source) — JAMAIS une règle fiscale devinée
/// (CLAUDE.md n°2). Une valeur de type source non cartographiée par le tenant reste
/// <see cref="Unmapped"/> : on ne devine pas.
/// </summary>
public enum SourceDocumentClassification
{
    /// <summary>
    /// Aucune correspondance tenant pour ce type source : NON classé. On ne devine pas (CLAUDE.md n°2) ;
    /// le repli est la détection STRUCTURELLE de l'avoir (référence d'origine EN 16931 BG-3). Valeur par
    /// défaut (état honnête « aucune correspondance provisionnée »).
    /// </summary>
    Unmapped = 0,

    /// <summary>Le type source correspond à une FACTURE (UNTDID 1001 « 380 »).</summary>
    Invoice = 1,

    /// <summary>Le type source correspond à un AVOIR (UNTDID 1001 « 381 »).</summary>
    CreditNote = 2,
}
