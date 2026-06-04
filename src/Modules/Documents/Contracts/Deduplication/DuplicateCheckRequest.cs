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

    /// <summary>Numéro du document (EN 16931 BT-1) — clé fonctionnelle F06 §4 avec le SIREN.</summary>
    public required string DocumentNumber { get; init; }

    /// <summary>Empreinte canonique du payload pivot (SHA-256 hex) — garde-fou anti ré-extraction (F06 §4.4).</summary>
    public required string PayloadHash { get; init; }
}
