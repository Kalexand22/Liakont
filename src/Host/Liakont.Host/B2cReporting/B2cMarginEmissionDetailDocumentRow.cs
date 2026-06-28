namespace Liakont.Host.B2cReporting;

using System;

/// <summary>Une pièce composant un lot d'émission e-reporting B2C (BUG-22) : lien vers le document + famille dérivée.</summary>
internal sealed record B2cMarginEmissionDetailDocumentRow
{
    /// <summary>Identifiant du document (lien vers sa fiche détail).</summary>
    public required Guid DocumentId { get; init; }

    /// <summary>Référence du document dans le système source.</summary>
    public required string SourceReference { get; init; }

    /// <summary>Famille de pièce dérivée de la référence source (BUG-20), « — » si non reconnue.</summary>
    public required string Family { get; init; }
}
