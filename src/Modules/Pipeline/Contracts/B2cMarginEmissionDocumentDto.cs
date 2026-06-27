namespace Liakont.Modules.Pipeline.Contracts;

using System;

/// <summary>Une pièce ayant composé un lot d'émission e-reporting B2C (BUG-22) : lien vers le document source.</summary>
public sealed record B2cMarginEmissionDocumentDto
{
    /// <summary>Identifiant du document (lien vers sa fiche détail).</summary>
    public required Guid DocumentId { get; init; }

    /// <summary>Référence du document dans le système source (ex. <c>encheresv6:ba:9000004</c>).</summary>
    public required string SourceReference { get; init; }
}
