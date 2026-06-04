namespace Liakont.Modules.Reconciliation.Contracts;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Reconciliation.Contracts.DTOs;

/// <summary>
/// Surface publique de LECTURE de la file d'attente de réconciliation (item TRK07), consommée
/// par l'API et la console (API04/WEB08). Les trois catégories de la file : propositions en attente,
/// PDF orphelins, documents émis sans PDF. TENANT-SCOPÉE par construction (database-per-tenant).
/// </summary>
public interface IReconciliationQueries
{
    /// <summary>Propositions de confiance moyenne EN ATTENTE de confirmation opérateur.</summary>
    Task<IReadOnlyList<ReconciliationProposalDto>> GetPendingProposalsAsync(CancellationToken cancellationToken = default);

    /// <summary>PDF ORPHELINS (aucune correspondance ou ambiguïté) — file d'attente manuelle.</summary>
    Task<IReadOnlyList<OrphanPdfDto>> GetOrphanPdfsAsync(CancellationToken cancellationToken = default);

    /// <summary>Documents ÉMIS pour lesquels aucun PDF n'a (encore) été rapproché.</summary>
    Task<IReadOnlyList<DocumentWithoutPdfDto>> GetIssuedDocumentsWithoutPdfAsync(CancellationToken cancellationToken = default);
}
