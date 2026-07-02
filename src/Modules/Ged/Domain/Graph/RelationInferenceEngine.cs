namespace Liakont.Modules.Ged.Domain.Graph;

using System;
using System.Collections.Generic;
using Liakont.Modules.Ged.Domain.Index;

/// <summary>
/// Moteur PUR d'inférence/héritage de relations entité↔entité (F19 §10, GED24). À partir d'une entité GRAINE et
/// du SUBSTRAT asserté (arêtes <c>direct</c>/<c>extracted</c> de son voisinage borné), il calcule les relations
/// DÉRIVÉES à appender (from = graine) :
/// <list type="bullet">
/// <item><description><b>Inférence</b> (mode <see cref="RelationInferenceMode.Transitive"/>) : fermeture
/// transitive d'un genre — A─k─▶B ─k─▶C ⇒ A─k─▶C, provenance <c>inferred</c>.</description></item>
/// <item><description><b>Héritage</b> (mode <see cref="RelationInferenceMode.Hierarchical"/>) : l'enfant hérite
/// les relations d'AUTRES genres de ses ancêtres — A─h─▶P (P parent de A) et P─k─▶C (k≠h) ⇒ A─k─▶C, provenance
/// <c>inherited</c>.</description></item>
/// </list>
/// <para>
/// GÉNÉRIQUE (aucun genre en dur : les genres/modes viennent des <paramref name="rules"/> tenant) et BORNÉ
/// (anti-DoS) : chaque parcours est un BFS borné par <see cref="RelationInferenceRule.MaxDepth"/> avec ensemble
/// <c>visited</c> ANTI-CYCLE — la terminaison est garantie même sur un graphe cyclique. Le substrat ne contient
/// QUE des relations ASSERTÉES (jamais de dérivées) : la fermeture CONVERGE (idempotente à travers les
/// exécutions). Les relations déjà courantes depuis la graine (<paramref name="existingOutRelations"/>) sont
/// exclues, et la même relation dérivée n'est émise qu'une fois (dédoublonnage intra-exécution).
/// </para>
/// </summary>
public static class RelationInferenceEngine
{
    /// <summary>
    /// Calcule les relations dérivées (from = <paramref name="seedEntityId"/>) à appender.
    /// </summary>
    /// <param name="seedEntityId">Entité graine dont on dérive les relations (émission per-seed bornée).</param>
    /// <param name="substrateEdges">Arêtes ASSERTÉES du voisinage borné de la graine (substrat traversé).</param>
    /// <param name="existingOutRelations">Relations DÉJÀ courantes depuis la graine, en <c>(cible, genre)</c> — exclues (idempotence).</param>
    /// <param name="rules">Règles d'inférence/héritage ACTIVES du tenant (genres/modes/bornes).</param>
    public static IReadOnlyList<DerivedRelation> Infer(
        Guid seedEntityId,
        IReadOnlyCollection<EntityRelationEdge> substrateEdges,
        IReadOnlyCollection<(Guid ToEntityId, string RelationKind)> existingOutRelations,
        IReadOnlyCollection<RelationInferenceRule> rules)
    {
        ArgumentNullException.ThrowIfNull(substrateEdges);
        ArgumentNullException.ThrowIfNull(existingOutRelations);
        ArgumentNullException.ThrowIfNull(rules);

        // Adjacence par (genre, source) → cibles, et par source → (cible, genre). Dédoublonnée (le substrat
        // multi-valeur peut porter plusieurs lignes identiques ; on ne visite chaque arête logique qu'une fois).
        var outByKind = new Dictionary<(string Kind, Guid From), HashSet<Guid>>();
        var outByFrom = new Dictionary<Guid, HashSet<(Guid To, string Kind)>>();

        foreach (var edge in substrateEdges)
        {
            if (!outByKind.TryGetValue((edge.RelationKind, edge.FromEntityId), out var kinded))
            {
                kinded = [];
                outByKind[(edge.RelationKind, edge.FromEntityId)] = kinded;
            }

            kinded.Add(edge.ToEntityId);

            if (!outByFrom.TryGetValue(edge.FromEntityId, out var fromAll))
            {
                fromAll = [];
                outByFrom[edge.FromEntityId] = fromAll;
            }

            fromAll.Add((edge.ToEntityId, edge.RelationKind));
        }

        // Clés déjà présentes (courantes depuis la graine) OU déjà émises : exclusion + dédoublonnage.
        var emittedOrExisting = new HashSet<(Guid To, string Kind)>(existingOutRelations);
        var results = new List<DerivedRelation>();

        void TryEmit(Guid to, string kind, string relationType)
        {
            if (to == seedEntityId)
            {
                return; // ck_er_no_self : jamais de relation réflexive.
            }

            if (emittedOrExisting.Add((to, kind)))
            {
                results.Add(new DerivedRelation(seedEntityId, to, kind, relationType));
            }
        }

        foreach (var rule in rules)
        {
            if (string.Equals(rule.Mode, RelationInferenceMode.Transitive, StringComparison.Ordinal))
            {
                InferTransitive(seedEntityId, rule, outByKind, TryEmit);
            }
            else if (string.Equals(rule.Mode, RelationInferenceMode.Hierarchical, StringComparison.Ordinal))
            {
                InferHierarchical(seedEntityId, rule, outByKind, outByFrom, TryEmit);
            }
        }

        return results;
    }

    // Fermeture transitive bornée du genre de la règle, depuis la graine : tout nœud atteint à distance ≥ 2
    // (les voisins directs, distance 1, sont déjà des arêtes assertées) devient une relation « inferred ».
    private static void InferTransitive(
        Guid seed,
        RelationInferenceRule rule,
        Dictionary<(string Kind, Guid From), HashSet<Guid>> outByKind,
        Action<Guid, string, string> tryEmit)
    {
        var visited = new HashSet<Guid> { seed };
        var queue = new Queue<(Guid Node, int Depth)>();
        queue.Enqueue((seed, 0));

        while (queue.Count > 0)
        {
            var (node, depth) = queue.Dequeue();
            if (depth >= rule.MaxDepth)
            {
                continue; // borne de profondeur (anti-DoS).
            }

            if (!outByKind.TryGetValue((rule.RelationKind, node), out var neighbours))
            {
                continue;
            }

            foreach (var next in neighbours)
            {
                if (!visited.Add(next))
                {
                    continue; // anti-cycle (déjà vu à une distance ≤).
                }

                queue.Enqueue((next, depth + 1));

                if (depth + 1 >= 2)
                {
                    tryEmit(next, rule.RelationKind, EntityRelation.InferredRelationType);
                }
            }
        }
    }

    // Héritage borné : remonte les ancêtres de la graine le long du genre PARENT de la règle ; à chaque ancêtre,
    // la graine hérite les arêtes assertées d'AUTRES genres de cet ancêtre (« inherited »).
    private static void InferHierarchical(
        Guid seed,
        RelationInferenceRule rule,
        Dictionary<(string Kind, Guid From), HashSet<Guid>> outByKind,
        Dictionary<Guid, HashSet<(Guid To, string Kind)>> outByFrom,
        Action<Guid, string, string> tryEmit)
    {
        var visited = new HashSet<Guid> { seed };
        var queue = new Queue<(Guid Node, int Depth)>();
        queue.Enqueue((seed, 0));

        while (queue.Count > 0)
        {
            var (node, depth) = queue.Dequeue();
            if (depth >= rule.MaxDepth)
            {
                continue; // borne de profondeur (anti-DoS).
            }

            if (!outByKind.TryGetValue((rule.RelationKind, node), out var parents))
            {
                continue;
            }

            foreach (var parent in parents)
            {
                if (!visited.Add(parent))
                {
                    continue; // anti-cycle.
                }

                queue.Enqueue((parent, depth + 1));

                // La graine hérite les relations d'AUTRES genres portées par cet ancêtre.
                if (!outByFrom.TryGetValue(parent, out var parentEdges))
                {
                    continue;
                }

                foreach (var (target, kind) in parentEdges)
                {
                    if (!string.Equals(kind, rule.RelationKind, StringComparison.Ordinal))
                    {
                        tryEmit(target, kind, EntityRelation.InheritedRelationType);
                    }
                }
            }
        }
    }
}
