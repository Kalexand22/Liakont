namespace Liakont.Modules.Ged.Infrastructure.Index;

/// <summary>
/// Issue d'une indexation GED (foyer UNIQUE d'écriture de l'index, partagé par le consommateur d'ingestion GED05b
/// et le backfill rétroactif GED10). Aucune ne représente une erreur : <see cref="Deferred"/> est un résultat
/// métier (DEFER, jamais BLOCK — INV-GED-05, règle 3) et <see cref="AlreadyPresent"/> est le no-op idempotent (RL-04).
/// </summary>
internal enum GedIndexOutcome
{
    /// <summary>Le document a été mappé et indexé (statut <c>indexed</c>).</summary>
    Indexed,

    /// <summary>Le document a été rangé en attente (statut <c>deferred</c>, motif français actionnable).</summary>
    Deferred,

    /// <summary>Le document était déjà indexé/déféré : no-op idempotent (replay ou re-backfill, RL-04).</summary>
    AlreadyPresent,
}
