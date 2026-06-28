namespace Liakont.Modules.Pipeline.Domain.B2cReporting;

/// <summary>
/// Motif de BLOCAGE (fail-closed, CLAUDE.md n°2/3) d'un document B2C-marge à la résolution de sa
/// contribution — jamais une marge fausse ni devinée. Chaque motif renvoie à un ancrage F03.
/// </summary>
public enum B2cMarginBlockReason
{
    /// <summary>Le document fait apparaître une TVA distincte (art. 297 E l'interdit — F03 §2.3/§2.5).</summary>
    SeparateVat = 1,

    /// <summary>Le document ne porte aucun honoraire (pas de marge à reporter).</summary>
    NoHonoraires = 2,

    /// <summary>Un honoraire porte un code TVA source non mappé par la table F03 (<c>defaultBehavior:block</c>).</summary>
    UnmappedRate = 3,

    /// <summary>Les honoraires d'une MÊME vente sont à des taux différents — découpage de la marge non sourcé (F03 §2.5).</summary>
    MixedRates = 4,

    /// <summary>
    /// L'adjudication (porteuse du signal « régime de la marge ») n'est plus mappable par la table validée
    /// depuis le CHECK (table absente, régime décroché, ou ligne hors forme) : impossible de classer le
    /// document, donc de résoudre sa marge. Tracé (miroir du HOLD <c>TvaUnresolved</c> de SendTenantJob),
    /// jamais un skip muet — le document reste <c>ReadyToSend</c>, repris quand la table est rétablie.
    /// </summary>
    AdjudicationNotMapped = 5,
}
