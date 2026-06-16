namespace Liakont.Modules.Documents.Contracts.Deduplication;

using System;

/// <summary>
/// Requête d'anti-doublon AVANT envoi (item TRK03, F06 §4), posée par le pipeline (PIP01) sur le tenant
/// COURANT (la connexion est la frontière de tenant — database-per-tenant, blueprint §7). Le candidat est
/// identifié par <see cref="DocumentId"/> afin d'être EXCLU de la recherche d'antécédents (un document ne
/// se compare jamais à lui-même).
/// </summary>
public sealed record DuplicateCheckRequest
{
    /// <summary>Identifiant du document candidat (exclu des antécédents).</summary>
    public required Guid DocumentId { get; init; }

    /// <summary>SIREN du fournisseur/émetteur (clé fonctionnelle F06 §4 avec le numéro). <c>null</c> si absent de la source.</summary>
    public string? SupplierSiren { get; init; }

    /// <summary>
    /// Identifiant du mandant en autofacturation (BT-3 = 389). <c>null</c> = cas GÉNÉRAL (non-389) : la clé
    /// fonctionnelle reste l'historique <c>(supplier_siren, document_number)</c>, INCHANGÉE. NON <c>null</c> = 389 :
    /// la clé bascule vers <c>(mandant_id, document_number)</c> — en 389 le « supplier » fiscal est le mandant
    /// (BT-30 = SIREN du mandant), pas l'émetteur matériel (F06 §4 amendé par ADR-0025 §6, F15 §3.2/§3.3).
    /// </summary>
    public Guid? MandantId { get; init; }

    /// <summary>
    /// Numéro du document — clé fonctionnelle F06 §4. En cas GÉNÉRAL : numéro source (EN 16931 BT-1) couplé au SIREN.
    /// En 389 (<see cref="MandantId"/> non <c>null</c>) : le <b>BT-1 fiscal ALLOUÉ par mandant</b> (MND05), PAS le
    /// numéro brut de la source (qui n'alimente que l'idempotence interne via <c>SourceReference</c> — ADR-0025 §1/§6).
    /// </summary>
    public required string DocumentNumber { get; init; }

    /// <summary>Empreinte canonique du payload pivot (SHA-256 hex) — garde-fou anti ré-extraction (F06 §4.4).</summary>
    public required string PayloadHash { get; init; }
}
