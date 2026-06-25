namespace Liakont.Modules.Pipeline.Domain.B2cReporting;

/// <summary>
/// Motif de blocage (fail-closed) de la résolution d'un document B2C taxable au régime du prix total
/// (<see cref="B2cTaxableResolver"/>, F03 §2.7). Jamais un envoi à l'aveugle (CLAUDE.md n°2/3) : un document
/// bloqué reste prêt à l'envoi, l'opérateur est informé (message FR), l'action corrective porte sur la donnée
/// source ou la table de mapping validée.
/// </summary>
public enum B2cTaxableBlockReason
{
    /// <summary>Aucune base taxable (ni adjudication ni commission acheteur) : rien à déclarer en TLB1.</summary>
    NoTaxableBase = 0,

    /// <summary>Un taux (adjudication ou commission) n'est pas résolu par la table validée — jamais deviné.</summary>
    UnmappedRate = 1,

    /// <summary>L'adjudication n'est plus mappable depuis le CHECK (table absente / régime décroché) — repris au prochain run.</summary>
    AdjudicationNotMapped = 2,
}
