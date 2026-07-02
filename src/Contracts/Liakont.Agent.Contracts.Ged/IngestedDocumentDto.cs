namespace Liakont.Agent.Contracts.Ged;

using System;
using System.Collections.Generic;

/// <summary>
/// Document NON-facture ingéré par le canal GED (F19 §4.2) — DTO PUR, DISJOINT du pivot fiscal
/// <c>PivotDocumentDto</c> (deux DTO, deux endpoints, un moteur partagé — §4.1). L'agent EXTRAIT BRUT
/// et DÉCLARE ; toute interprétation (mapping d'axes/entités/relations, résolution d'identité, DEFER)
/// vit sur la PLATEFORME (CLAUDE.md n°6). Symétrie pivot « champ absent → <c>null</c> ». Aucune règle
/// fiscale, aucune logique métier ici. L'empreinte canonique (<c>GedCanonicalJson</c> +
/// <c>PayloadHasher</c>) sert l'anti-doublon et la détection d'altération du canal GED, dans un espace
/// de hash STRICTEMENT SÉPARÉ du canal fiscal (registre <c>ged_ingestion</c> dédié — §4.3.1).
/// </summary>
public sealed class IngestedDocumentDto
{
    private static readonly IReadOnlyDictionary<string, string> EmptyFields =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Crée un document GED ingéré.</summary>
    /// <param name="sourceReference">
    /// Identifiant du document dans le système source — clé de réconciliation ET de détection d'altération
    /// (obligatoire). Idempotence : deux extractions d'un même document portent le même
    /// <paramref name="sourceReference"/>.
    /// </param>
    /// <param name="documentType">
    /// Type de document dans la source, valeur BRUTE (jamais classée par l'agent) — la plateforme y associe
    /// un <c>GedMappingProfile</c> ou range le document en <c>deferred</c> (§4.5), jamais deviné.
    /// </param>
    /// <param name="sourceTimestampUtc">
    /// Horodatage UTC de la source (émission/dépôt), ou <c>null</c> si la source n'en fournit pas. Donnée
    /// CONTRACTUELLE de la source, jamais inventée (CLAUDE.md n°2). Émis au format
    /// <c>yyyy-MM-ddTHH:mm:ssZ</c> (ADR-0007) quand porté ; OMIS quand <c>null</c> (hash inchangé).
    /// </param>
    /// <param name="content">
    /// Référence au contenu binaire (§4.2), ou <c>null</c> si la source ne porte aucun binaire (métadonnées
    /// seules). Le rangement write-once probant est un concern PLATEFORME (§4.3.2 / §5.1).
    /// </param>
    /// <param name="sourceFields">
    /// Champs BRUTS de la source (nom → valeur, non interprétés) — PAS un EAV plateforme. ÉMIS TRIÉ PAR CLÉ
    /// (ordinal) par <c>GedCanonicalJson</c> : sans tri, l'anti-doublon (tenant, hash) casserait selon
    /// l'ordre d'itération du dictionnaire (RL-39). Jamais nul (coalescé en dictionnaire vide).
    /// </param>
    /// <param name="sourceAxes">Indices d'axes BRUTS observés dans la source (§4.2) ; jamais nul (coalescé vide).</param>
    /// <param name="sourceEntities">Indices d'entités BRUTS observés dans la source (§4.2) ; jamais nul (coalescé vide).</param>
    /// <param name="sourceRelations">Indices de relations BRUTS observés dans la source (§4.2) ; jamais nul (coalescé vide).</param>
    public IngestedDocumentDto(
        string sourceReference,
        string documentType,
        DateTime? sourceTimestampUtc = null,
        IngestedContentRef? content = null,
        IReadOnlyDictionary<string, string>? sourceFields = null,
        IReadOnlyList<RawAxisHint>? sourceAxes = null,
        IReadOnlyList<RawEntityHint>? sourceEntities = null,
        IReadOnlyList<RawRelationHint>? sourceRelations = null)
    {
        SourceReference = sourceReference;
        DocumentType = documentType;
        SourceTimestampUtc = sourceTimestampUtc;
        Content = content;
        SourceFields = sourceFields ?? EmptyFields;
        SourceAxes = sourceAxes ?? Array.Empty<RawAxisHint>();
        SourceEntities = sourceEntities ?? Array.Empty<RawEntityHint>();
        SourceRelations = sourceRelations ?? Array.Empty<RawRelationHint>();
    }

    /// <summary>Identifiant du document dans le système source (réconciliation + altération, obligatoire).</summary>
    public string SourceReference { get; }

    /// <summary>Type de document dans la source, BRUT (jamais classé par l'agent).</summary>
    public string DocumentType { get; }

    /// <summary>Horodatage UTC de la source (<c>null</c> si absent).</summary>
    public DateTime? SourceTimestampUtc { get; }

    /// <summary>Référence au contenu binaire (<c>null</c> si pas de binaire).</summary>
    public IngestedContentRef? Content { get; }

    /// <summary>Champs BRUTS de la source (nom → valeur), émis TRIÉS PAR CLÉ (ordinal) — RL-39.</summary>
    public IReadOnlyDictionary<string, string> SourceFields { get; }

    /// <summary>Indices d'axes BRUTS observés dans la source.</summary>
    public IReadOnlyList<RawAxisHint> SourceAxes { get; }

    /// <summary>Indices d'entités BRUTS observés dans la source.</summary>
    public IReadOnlyList<RawEntityHint> SourceEntities { get; }

    /// <summary>Indices de relations BRUTS observés dans la source.</summary>
    public IReadOnlyList<RawRelationHint> SourceRelations { get; }
}
