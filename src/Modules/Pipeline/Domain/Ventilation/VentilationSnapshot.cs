namespace Liakont.Modules.Pipeline.Domain.Ventilation;

using System;
using System.Collections.Generic;
using Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Snapshot requêtable de la ventilation TVA par taux d'un document (ADR-0015), capturé au CHECK
/// (PIP01b) quand la ventilation SOURCÉE et validée par le mapping est calculée, juste avant
/// <c>MarkReadyToSend</c>. Persistance dédiée, tenant-scopée, APPEND-ONLY, versionnée par
/// <see cref="MappingVersion"/> — DISTINCTE du staging (purgé après Issued, ADR-0014) et du coffre WORM
/// (immuable). Elle SURVIT à la purge du staging pour rendre l'agrégation de paiement (PIP03a) possible
/// après l'émission. Ne porte QUE la sortie du mapping validé (INV-VENTILATION-001) ; ne classe PAS
/// frais/adjudication d'un <see cref="OperationCategory.Mixte"/> (réservé à PIP03b).
/// </summary>
public sealed record VentilationSnapshot
{
    /// <summary>Identifiant du document (clé d'unicité avec <see cref="MappingVersion"/>).</summary>
    public required Guid DocumentId { get; init; }

    /// <summary>Numéro de document (EN 16931 BT-1) — clé de rattachement d'un encaissement (RelatedDocumentNumber).</summary>
    public required string DocumentNumber { get; init; }

    /// <summary>Référence source du document (audit / réconciliation).</summary>
    public required string SourceReference { get; init; }

    /// <summary>Nature de l'opération du document (PrestationServices déclenche l'e-reporting de paiement ; Mixte suspendu).</summary>
    public required OperationCategory OperationCategory { get; init; }

    /// <summary>Version de la table de mapping sous laquelle la ventilation a été calculée (provenance, happened-before).</summary>
    public required string MappingVersion { get; init; }

    /// <summary>Ventilation par taux (base HT + TVA) telle que produite par le mapping validé.</summary>
    public required IReadOnlyList<VentilationLine> Lines { get; init; }

    /// <summary>Horodatage de capture (UTC).</summary>
    public required DateTimeOffset CreatedUtc { get; init; }
}
