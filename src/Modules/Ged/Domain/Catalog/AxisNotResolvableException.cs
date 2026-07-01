namespace Liakont.Modules.Ged.Domain.Catalog;

using System;

/// <summary>
/// Levée par le handler d'écriture (GED04) quand un axe visé est INCONNU ou INACTIF dans le catalogue GED
/// (F19 §3.7, règle 2 : <b>refus, jamais deviner</b>). Le message opérateur porte le numéro de document et
/// l'action corrective (règle 12) : on ne crée jamais un axe implicitement pour « faire passer » une valeur.
/// </summary>
public sealed class AxisNotResolvableException : Exception
{
    public AxisNotResolvableException(string axisCode, Guid managedDocumentId)
        : base($"Axe GED « {axisCode} » inconnu ou inactif pour le document {managedDocumentId} : "
            + "déclarez l'axe (ou réactivez-le) dans le catalogue avant d'y porter une valeur "
            + "(jamais deviner, CLAUDE.md n.2).")
    {
        AxisCode = axisCode;
        ManagedDocumentId = managedDocumentId;
    }

    /// <summary>Code d'axe non résolvable.</summary>
    public string AxisCode { get; }

    /// <summary>Document géré sur lequel la valeur était portée.</summary>
    public Guid ManagedDocumentId { get; }
}
