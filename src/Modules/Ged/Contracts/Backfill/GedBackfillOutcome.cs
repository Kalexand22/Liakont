namespace Liakont.Modules.Ged.Contracts.Backfill;

/// <summary>
/// Issue du backfill rétroactif d'UN document du corpus fiscal déjà scellé dans l'index GED (GED10, F19 §11 D12).
/// Aucune ne représente une erreur : <see cref="Deferred"/> est le cas nominal d'un document sans profil de mapping
/// (DEFER plutôt que deviner, règle 3) et <see cref="AlreadyPresent"/> est le no-op idempotent d'un re-passage (RL-21).
/// </summary>
public enum GedBackfillOutcome
{
    /// <summary>Le document a été mappé et indexé (un profil validé couvre son type).</summary>
    Indexed,

    /// <summary>Le document a été rangé en attente (aucun profil pour son type, ou donnée non résolue).</summary>
    Deferred,

    /// <summary>Le document était déjà indexé (re-backfill idempotent) : aucune écriture (RL-21).</summary>
    AlreadyPresent,
}
