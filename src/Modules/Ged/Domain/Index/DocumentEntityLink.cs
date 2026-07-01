namespace Liakont.Modules.Ged.Domain.Index;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Lien document↔entité à APPENDER dans <c>ged_index.document_entity_links</c> (F19 §3.4.5/§4.5, item GED05b) : il
/// rattache un <c>managed_document</c> à une instance d'entité résolue dans un <c>role</c> métier DÉCLARÉ (paramétrage
/// tenant, jamais un enum figé — règle 7). APPEND-ONLY : un lien erroné se corrige par chaînage/rétractation, jamais
/// par UPDATE/DELETE (le trigger l'interdit). GED05b n'écrit que des liens de valeur normale (<c>is_retraction=false</c>,
/// <c>supersedes_id=null</c>). <c>relation_type</c> et <c>source</c> sont des vocabulaires TECHNIQUES fermés (miroir
/// des CHECK). Ni <c>id</c> ni <c>seq</c> ne sont portés ici (assignés par la base).
/// </summary>
public sealed class DocumentEntityLink
{
    /// <summary>Provenances autorisées, miroir Domain de <c>ck_del_source</c>.</summary>
    public static readonly IReadOnlyList<string> AllowedSources = ["agent", "manual", "ai", "import", "ocr"];

    /// <summary>Types de relation techniques autorisés, miroir Domain de <c>ck_del_relation_type</c>.</summary>
    public static readonly IReadOnlyList<string> AllowedRelationTypes = ["direct", "inferred", "extracted", "inherited"];

    /// <summary>Crée un lien document↔entité à appender.</summary>
    public DocumentEntityLink(
        Guid managedDocumentId,
        Guid entityId,
        string role,
        string source,
        string relationType = "direct",
        decimal? confidenceScore = null,
        string? operatorIdentity = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(relationType);

        if (!AllowedSources.Contains(source, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                $"Source de lien d'entité GED « {source} » invalide : attendu l'une de [{string.Join(", ", AllowedSources)}] "
                    + "(miroir ck_del_source, jamais deviner CLAUDE.md n.2).",
                nameof(source));
        }

        if (!AllowedRelationTypes.Contains(relationType, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                $"Type de relation GED « {relationType} » invalide : attendu l'une de [{string.Join(", ", AllowedRelationTypes)}] "
                    + "(miroir ck_del_relation_type).",
                nameof(relationType));
        }

        if (confidenceScore is < 0m or > 1m)
        {
            throw new ArgumentException(
                $"Score de confiance de lien d'entité GED ({confidenceScore}) hors de l'intervalle [0..1] (miroir ck_del_confidence).",
                nameof(confidenceScore));
        }

        ManagedDocumentId = managedDocumentId;
        EntityId = entityId;
        Role = role;
        Source = source;
        RelationType = relationType;
        ConfidenceScore = confidenceScore;
        OperatorIdentity = operatorIdentity;
    }

    /// <summary>Document géré rattaché (<c>managed_document_id</c>, soft-link).</summary>
    public Guid ManagedDocumentId { get; }

    /// <summary>Instance d'entité rattachée (<c>entity_id</c>, soft-link → <c>entity_instances.id</c>).</summary>
    public Guid EntityId { get; }

    /// <summary>Rôle métier DÉCLARÉ du rattachement (paramétrage tenant, ex. <c>acheteur</c>/<c>concerne</c>).</summary>
    public string Role { get; }

    /// <summary>Provenance du lien (<c>agent|manual|ai|import|ocr</c>).</summary>
    public string Source { get; }

    /// <summary>Type technique de relation (<c>direct|inferred|extracted|inherited</c>).</summary>
    public string RelationType { get; }

    /// <summary>Score de confiance [0..1] ; <see langword="null"/> si déterministe.</summary>
    public decimal? ConfidenceScore { get; }

    /// <summary>Identité de l'opérateur (présente si <c>source='manual'</c>).</summary>
    public string? OperatorIdentity { get; }
}
