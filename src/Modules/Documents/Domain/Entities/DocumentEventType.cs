namespace Liakont.Modules.Documents.Domain.Entities;

/// <summary>
/// Type d'un <see cref="DocumentEvent"/> de la piste d'audit (F06 §3). Périmètre TRK01 : seul l'événement
/// de GENÈSE (<see cref="DocumentDetected"/>) est défini — c'est le premier fait d'audit, écrit à la
/// création du document par l'ingestion. Les types liés aux transitions d'état (TRK02), à l'altération
/// source après émission (TRK03) et aux snapshots d'émission/rejet (TRK04) s'ajoutent avec ces items.
/// </summary>
/// <remarks>
/// Persisté en TEXTE (nom de l'énumération) dans la colonne <c>event_type</c> — même motif de lisibilité
/// d'audit que <see cref="DocumentState"/>.
/// </remarks>
public enum DocumentEventType
{
    /// <summary>Document détecté/créé en état <c>Detected</c> par l'ingestion (genèse de la piste d'audit).</summary>
    DocumentDetected,
}
