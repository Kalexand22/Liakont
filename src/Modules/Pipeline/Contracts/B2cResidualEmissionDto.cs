namespace Liakont.Modules.Pipeline.Contracts;

using System;

/// <summary>
/// Dernière émission ISSUED d'un document — lot d'émission + référence de la pièce source — nécessaire au
/// RATTRAPAGE de l'état résiduel « émission acceptée mais document resté ReadyToSend » (ADR-0037 D3). La
/// <see cref="SourceReference"/> permet de rejouer le gel du lien reporting↔pièce (D2, idempotent) en même
/// temps que la transition d'état, sans re-transmission.
/// </summary>
public sealed record B2cResidualEmissionDto
{
    /// <summary>Lot de la DERNIÈRE transmission <c>Issued</c> qui a inclus le document.</summary>
    public required Guid EmissionBatchId { get; init; }

    /// <summary>Référence de la pièce source portée par cette émission (gel du lien reporting↔pièce).</summary>
    public required string SourceReference { get; init; }
}
