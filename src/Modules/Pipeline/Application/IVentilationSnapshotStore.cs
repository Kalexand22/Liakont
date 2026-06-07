namespace Liakont.Modules.Pipeline.Application;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Pipeline.Domain.Ventilation;

/// <summary>
/// Écriture et lecture du snapshot de ventilation TVA (ADR-0015, <c>pipeline.ventilation_snapshots</c>).
/// Écrit au CHECK (PIP01b) quand la ventilation sourcée est calculée, lu par l'agrégateur de paiement
/// (PIP03a) APRÈS la purge du staging. TENANT-SCOPÉ : la connexion EST le tenant (database-per-tenant,
/// blueprint §7) — aucun accès cross-tenant. APPEND-ONLY, versionné par <c>mapping_version</c>
/// (INV-VENTILATION-003) : l'écriture est IDEMPOTENTE sur (document_id, mapping_version) ; un re-CHECK
/// du même document à la même version n'insère pas de doublon, un re-mapping ajoute une nouvelle version.
/// </summary>
public interface IVentilationSnapshotStore
{
    /// <summary>
    /// Persiste un snapshot de façon IDEMPOTENTE sur (document_id, mapping_version). Retourne <c>true</c>
    /// si le snapshot a été inséré, <c>false</c> s'il existait déjà (re-CHECK idempotent).
    /// </summary>
    Task<bool> SaveAsync(VentilationSnapshot snapshot, CancellationToken cancellationToken = default);

    /// <summary>
    /// Snapshot d'un document pour une version de mapping donnée (celle liée à l'émission —
    /// <c>Document.MappingVersion</c>, happened-before ADR-0015 §4), ou <c>null</c> s'il n'existe pas.
    /// </summary>
    Task<VentilationSnapshot?> GetAsync(Guid documentId, string mappingVersion, CancellationToken cancellationToken = default);
}
