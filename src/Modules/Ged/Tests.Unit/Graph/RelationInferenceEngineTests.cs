namespace Liakont.Modules.Ged.Tests.Unit.Graph;

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Liakont.Modules.Ged.Domain.Graph;
using Liakont.Modules.Ged.Domain.Index;
using Xunit;

/// <summary>
/// Tests du moteur PUR d'inférence/héritage (F19 §10, GED24) : fermeture transitive et héritage hiérarchique,
/// tous deux BORNÉS (profondeur), ANTI-CYCLE, GÉNÉRIQUES (genres arbitraires, aucun métier en dur) et IDEMPOTENTS
/// (exclusion des relations déjà courantes). Le moteur émet des relations DÉRIVÉES depuis la seule graine.
/// </summary>
public sealed class RelationInferenceEngineTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static readonly Guid C = Guid.NewGuid();
    private static readonly Guid D = Guid.NewGuid();

    private static RelationInferenceRule[] Rules(params RelationInferenceRule[] rules) => rules;

    private static (Guid To, string Kind)[] Existing(params (Guid To, string Kind)[] pairs) => pairs;

    // ─────────────────────────── Inférence transitive ───────────────────────────

    [Fact]
    public void Transitive_infers_the_indirect_relation_but_not_the_direct_ones()
    {
        // A ─k─▶ B ─k─▶ C  ⇒  A ─k─▶ C (inferred). Les arêtes directes (A→B, B→C) ne sont PAS ré-émises.
        var substrate = new[]
        {
            new EntityRelationEdge(A, B, "k"),
            new EntityRelationEdge(B, C, "k"),
        };

        var derived = RelationInferenceEngine.Infer(
            A, substrate, Existing((B, "k")), Rules(new RelationInferenceRule("k", RelationInferenceMode.Transitive, 3)));

        derived.Should().ContainSingle();
        derived[0].Should().Be(new DerivedRelation(A, C, "k", EntityRelation.InferredRelationType));
    }

    [Fact]
    public void Transitive_respects_the_depth_bound()
    {
        // A→B→C→D, borne = 2 : A→C inféré (distance 2), A→D JAMAIS (distance 3, hors borne — anti-DoS).
        var substrate = new[]
        {
            new EntityRelationEdge(A, B, "k"),
            new EntityRelationEdge(B, C, "k"),
            new EntityRelationEdge(C, D, "k"),
        };

        var derived = RelationInferenceEngine.Infer(
            A, substrate, Existing((B, "k")), Rules(new RelationInferenceRule("k", RelationInferenceMode.Transitive, 2)));

        derived.Should().ContainSingle().Which.ToEntityId.Should().Be(C);
        derived.Should().NotContain(r => r.ToEntityId == D);
    }

    [Fact]
    public void Transitive_terminates_on_a_cycle_and_never_emits_a_self_relation()
    {
        // Cycle A→B→C→A, borne large : la terminaison est garantie (anti-cycle) ; A→C est inféré, jamais A→A.
        var substrate = new[]
        {
            new EntityRelationEdge(A, B, "k"),
            new EntityRelationEdge(B, C, "k"),
            new EntityRelationEdge(C, A, "k"),
        };

        var derived = RelationInferenceEngine.Infer(
            A, substrate, Existing((B, "k")), Rules(new RelationInferenceRule("k", RelationInferenceMode.Transitive, 8)));

        derived.Should().ContainSingle().Which.Should().Be(new DerivedRelation(A, C, "k", EntityRelation.InferredRelationType));
        derived.Should().NotContain(r => r.ToEntityId == A);
    }

    [Fact]
    public void Transitive_deduplicates_a_target_reachable_by_two_paths()
    {
        // A→B→D et A→C→D : A→D n'est émis QU'UNE fois (dédoublonnage).
        var substrate = new[]
        {
            new EntityRelationEdge(A, B, "k"),
            new EntityRelationEdge(A, C, "k"),
            new EntityRelationEdge(B, D, "k"),
            new EntityRelationEdge(C, D, "k"),
        };

        var derived = RelationInferenceEngine.Infer(
            A, substrate, Existing((B, "k"), (C, "k")), Rules(new RelationInferenceRule("k", RelationInferenceMode.Transitive, 4)));

        derived.Should().ContainSingle().Which.ToEntityId.Should().Be(D);
    }

    [Fact]
    public void Transitive_excludes_a_relation_that_is_already_current()
    {
        // A→C existe déjà (courante) : l'inférence ne la ré-appende pas (idempotence).
        var substrate = new[]
        {
            new EntityRelationEdge(A, B, "k"),
            new EntityRelationEdge(B, C, "k"),
        };

        var derived = RelationInferenceEngine.Infer(
            A, substrate, Existing((B, "k"), (C, "k")), Rules(new RelationInferenceRule("k", RelationInferenceMode.Transitive, 3)));

        derived.Should().BeEmpty();
    }

    // ─────────────────────────── Héritage hiérarchique ───────────────────────────

    [Fact]
    public void Hierarchical_inherits_the_parent_relations_of_other_kinds()
    {
        // A ─h─▶ P (P parent de A) et P ─k─▶ C  ⇒  A hérite  A ─k─▶ C (inherited). L'arête parent (h) n'est PAS héritée.
        var p = Guid.NewGuid();
        var substrate = new[]
        {
            new EntityRelationEdge(A, p, "h"),
            new EntityRelationEdge(p, C, "k"),
        };

        var derived = RelationInferenceEngine.Infer(
            A, substrate, Existing((p, "h")), Rules(new RelationInferenceRule("h", RelationInferenceMode.Hierarchical, 3)));

        derived.Should().ContainSingle().Which.Should().Be(new DerivedRelation(A, C, "k", EntityRelation.InheritedRelationType));
        derived.Should().NotContain(r => r.RelationKind == "h");
    }

    [Fact]
    public void Hierarchical_inherits_across_several_ancestor_levels_within_the_bound()
    {
        // A ─h─▶ P ─h─▶ GP, GP ─k─▶ X, borne = 3 : A hérite  A ─k─▶ X (via l'ancêtre GP).
        var p = Guid.NewGuid();
        var gp = Guid.NewGuid();
        var x = Guid.NewGuid();
        var substrate = new[]
        {
            new EntityRelationEdge(A, p, "h"),
            new EntityRelationEdge(p, gp, "h"),
            new EntityRelationEdge(gp, x, "k"),
        };

        var derived = RelationInferenceEngine.Infer(
            A, substrate, Existing((p, "h")), Rules(new RelationInferenceRule("h", RelationInferenceMode.Hierarchical, 3)));

        derived.Should().ContainSingle().Which.Should().Be(new DerivedRelation(A, x, "k", EntityRelation.InheritedRelationType));
    }

    [Fact]
    public void Hierarchical_respects_the_ancestor_depth_bound()
    {
        // Même graphe, borne = 1 : seul l'ancêtre direct P est visité ; l'arête de GP n'est PAS héritée.
        var p = Guid.NewGuid();
        var gp = Guid.NewGuid();
        var x = Guid.NewGuid();
        var substrate = new[]
        {
            new EntityRelationEdge(A, p, "h"),
            new EntityRelationEdge(p, gp, "h"),
            new EntityRelationEdge(gp, x, "k"),
        };

        var derived = RelationInferenceEngine.Infer(
            A, substrate, Existing((p, "h")), Rules(new RelationInferenceRule("h", RelationInferenceMode.Hierarchical, 1)));

        derived.Should().BeEmpty("l'unique ancêtre atteignable dans la borne (P) ne porte aucune relation d'un autre genre");
    }

    // ─────────────────────────── Généricité & no-op ───────────────────────────

    [Fact]
    public void Engine_is_generic_over_arbitrary_relation_kinds()
    {
        // Aucun genre en dur : le moteur propage exactement les genres déclarés, quels qu'ils soient.
        const string oddKind = "genre_totalement_arbitraire_42";
        var substrate = new[]
        {
            new EntityRelationEdge(A, B, oddKind),
            new EntityRelationEdge(B, C, oddKind),
        };

        var derived = RelationInferenceEngine.Infer(
            A, substrate, Existing((B, oddKind)), Rules(new RelationInferenceRule(oddKind, RelationInferenceMode.Transitive, 3)));

        derived.Should().ContainSingle().Which.RelationKind.Should().Be(oddKind);
    }

    [Fact]
    public void No_active_rule_yields_no_derived_relation()
    {
        var substrate = new[]
        {
            new EntityRelationEdge(A, B, "k"),
            new EntityRelationEdge(B, C, "k"),
        };

        var derived = RelationInferenceEngine.Infer(A, substrate, Existing((B, "k")), Rules());

        derived.Should().BeEmpty();
    }

    [Fact]
    public void Empty_substrate_yields_no_derived_relation()
    {
        var derived = RelationInferenceEngine.Infer(
            A,
            Array.Empty<EntityRelationEdge>(),
            Existing(),
            Rules(new RelationInferenceRule("k", RelationInferenceMode.Transitive, 3)));

        derived.Should().BeEmpty();
    }
}
