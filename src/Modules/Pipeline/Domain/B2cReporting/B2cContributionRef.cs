namespace Liakont.Modules.Pipeline.Domain.B2cReporting;

using System;

/// <summary>
/// Référence d'un document ayant contribué à un agrégat e-reporting (traçabilité reporting ↔ pièces,
/// réversibilité N→1 « retrouver les N pièces » — append-only en aval).
/// </summary>
public sealed record B2cContributionRef
{
    /// <summary>Identifiant du document source.</summary>
    public required Guid DocumentId { get; init; }

    /// <summary>Référence de la pièce source (ADR-0007).</summary>
    public required string SourceReference { get; init; }
}
