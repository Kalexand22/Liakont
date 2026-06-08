namespace Liakont.Host.Reconciliation;

using System.Collections.Generic;
using Liakont.Modules.Reconciliation.Contracts.DTOs;

/// <summary>
/// Modèle de LECTURE de la page Réconciliation (WEB08) : les trois files d'attente du module
/// Reconciliation (TRK07/API04) assemblées pour le tenant courant — propositions de confiance moyenne à
/// confirmer, PDF orphelins à rattacher manuellement, documents émis sans PDF rapproché. Aucun montant
/// recalculé ni règle métier : les DTO du module sont reportés tels quels (la page reste présentationnelle,
/// CLAUDE.md n°19). Tenant-scopé par construction (la connexion EST le tenant, CLAUDE.md n°9).
/// </summary>
public sealed record ReconciliationQueueViewModel
{
    /// <summary>Propositions de confiance moyenne EN ATTENTE de confirmation (ou de rejet) par l'opérateur.</summary>
    public required IReadOnlyList<ReconciliationProposalDto> Proposals { get; init; }

    /// <summary>PDF ORPHELINS : aucune correspondance automatique — à rattacher manuellement à un document.</summary>
    public required IReadOnlyList<OrphanPdfDto> Orphans { get; init; }

    /// <summary>Documents ÉMIS pour lesquels aucun PDF n'a (encore) été rapproché — candidats au lien manuel.</summary>
    public required IReadOnlyList<DocumentWithoutPdfDto> DocumentsWithoutPdf { get; init; }

    /// <summary>File entièrement vide (aucune des trois catégories) — état « rien à traiter ».</summary>
    public bool IsEmpty => Proposals.Count == 0 && Orphans.Count == 0 && DocumentsWithoutPdf.Count == 0;

    /// <summary>File VIDE (les trois catégories vides) — modèle partagé par défaut (accès refusé, pré-chargement).</summary>
    public static ReconciliationQueueViewModel Empty { get; } = new()
    {
        Proposals = [],
        Orphans = [],
        DocumentsWithoutPdf = [],
    };
}
