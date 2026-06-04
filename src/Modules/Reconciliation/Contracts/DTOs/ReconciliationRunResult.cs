namespace Liakont.Modules.Reconciliation.Contracts.DTOs;

/// <summary>
/// Bilan d'une passe de réconciliation pour un tenant (item TRK07) : nombre de PDF du pool nouvellement
/// traités, et leur répartition (liés automatiquement, proposés, orphelins). Les PDF déjà présents dans
/// la file d'attente ne sont pas re-traités.
/// </summary>
public sealed record ReconciliationRunResult(int Processed, int AutoLinked, int Proposed, int Orphans);
