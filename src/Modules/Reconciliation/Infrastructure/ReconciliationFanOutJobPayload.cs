namespace Liakont.Modules.Reconciliation.Infrastructure;

/// <summary>
/// Charge utile (vide) du job SYSTÈME de réconciliation : il s'applique à TOUS les tenants actifs, sans
/// paramètre. Déclenché à la demande (action opérateur API04) ou périodiquement (planification du module
/// Job), et après réception de PDF du pool (hook d'ingestion) — voir <c>MODULE.md</c>.
/// </summary>
public sealed record ReconciliationFanOutJobPayload();
