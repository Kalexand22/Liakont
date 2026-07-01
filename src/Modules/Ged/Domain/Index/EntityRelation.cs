namespace Liakont.Modules.Ged.Domain.Index;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Relation entité↔entité à APPENDER dans <c>ged_index.entity_relations</c> (F19 §3.4.4/§3.7) — append PUR
/// (<c>is_retraction=false</c>, <c>supersedes_id=null</c> : la dévalidation/rétractation par chaînage relève de
/// l'UoW et d'items ultérieurs, comme <see cref="DocumentAxisLink"/>). GED24 écrit des relations DÉRIVÉES
/// (<c>relation_type='inferred'|'inherited'</c>), mais le modèle accepte tout <see cref="AllowedRelationTypes"/>
/// (miroir <c>ck_er_relation_type</c>). Ni <c>id</c> ni <c>seq</c> ne sont portés ici : la base les assigne.
/// </summary>
public sealed class EntityRelation
{
    /// <summary>Provenance d'une relation issue d'une fermeture transitive (GED24).</summary>
    public const string InferredRelationType = "inferred";

    /// <summary>Provenance d'une relation issue d'un héritage hiérarchique (GED24).</summary>
    public const string InheritedRelationType = "inherited";

    /// <summary>Sources autorisées, miroir Domain de <c>ck_er_source</c>.</summary>
    public static readonly IReadOnlyList<string> AllowedSources = ["agent", "manual", "ai", "import", "ocr"];

    /// <summary>Provenances (types de relation) autorisées, miroir Domain de <c>ck_er_relation_type</c>.</summary>
    public static readonly IReadOnlyList<string> AllowedRelationTypes = ["direct", "inferred", "extracted", "inherited"];

    public EntityRelation(
        Guid fromEntityId,
        Guid toEntityId,
        string relationKind,
        string relationType,
        string source,
        decimal? confidenceScore = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relationKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(relationType);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        if (fromEntityId == toEntityId)
        {
            // Miroir Domain de ck_er_no_self : une entité n'a pas de relation vers elle-même.
            throw new ArgumentException(
                $"Relation GED réflexive interdite (from == to == {fromEntityId:D}) — miroir ck_er_no_self.",
                nameof(toEntityId));
        }

        if (!AllowedRelationTypes.Contains(relationType, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                $"Type de relation GED « {relationType} » invalide : attendu l'une de "
                    + $"[{string.Join(", ", AllowedRelationTypes)}] (miroir ck_er_relation_type, jamais deviner CLAUDE.md n.2).",
                nameof(relationType));
        }

        if (!AllowedSources.Contains(source, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                $"Source de relation GED « {source} » invalide : attendu l'une de "
                    + $"[{string.Join(", ", AllowedSources)}] (miroir ck_er_source, jamais deviner CLAUDE.md n.2).",
                nameof(source));
        }

        if (confidenceScore is < 0m or > 1m)
        {
            throw new ArgumentException(
                $"Score de confiance de relation GED ({confidenceScore}) hors de l'intervalle [0..1] "
                    + "(miroir ck_er_confidence).",
                nameof(confidenceScore));
        }

        FromEntityId = fromEntityId;
        ToEntityId = toEntityId;
        RelationKind = relationKind;
        RelationType = relationType;
        Source = source;
        ConfidenceScore = confidenceScore;
    }

    /// <summary>Entité source (<c>from_entity_id</c>, soft-link).</summary>
    public Guid FromEntityId { get; }

    /// <summary>Entité cible (<c>to_entity_id</c>, soft-link).</summary>
    public Guid ToEntityId { get; }

    /// <summary>Genre métier déclaré (<c>relation_kind</c>, paramétrage tenant).</summary>
    public string RelationKind { get; }

    /// <summary>Provenance (<c>direct|inferred|extracted|inherited</c>).</summary>
    public string RelationType { get; }

    /// <summary>Canal de provenance (<c>agent|manual|ai|import|ocr</c>).</summary>
    public string Source { get; }

    /// <summary>Score de confiance [0..1] ; <see langword="null"/> si déterministe (cas des relations dérivées).</summary>
    public decimal? ConfidenceScore { get; }
}
