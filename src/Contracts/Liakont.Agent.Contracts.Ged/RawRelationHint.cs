namespace Liakont.Agent.Contracts.Ged;

/// <summary>
/// Indice de relation BRUT observé par l'agent dans la source (F19 §4.2) — DTO PUR. L'agent DÉCLARE
/// une relation entre le document et une entité cible (type de relation, identifiant et type de la
/// cible) telle quelle ; il ne crée AUCUN lien plateforme (aucune interprétation, CLAUDE.md n°6). Le
/// mapping <c>relation source → relation cible</c> (§4.5, <c>RelationMappingRule</c>) et l'écriture
/// des liens append-only vivent sur la PLATEFORME.
/// </summary>
public sealed class RawRelationHint
{
    /// <summary>Crée un indice de relation brut.</summary>
    /// <param name="type">Type de relation BRUT tel que déclaré par la source (jamais une relation cible plateforme).</param>
    /// <param name="targetExternalId">Identifiant externe BRUT de l'entité/du document cible de la relation.</param>
    /// <param name="targetType">Type BRUT de la cible de la relation.</param>
    public RawRelationHint(string type, string targetExternalId, string targetType)
    {
        Type = type;
        TargetExternalId = targetExternalId;
        TargetType = targetType;
    }

    /// <summary>Type de relation BRUT tel que déclaré par la source.</summary>
    public string Type { get; }

    /// <summary>Identifiant externe BRUT de la cible de la relation.</summary>
    public string TargetExternalId { get; }

    /// <summary>Type BRUT de la cible de la relation.</summary>
    public string TargetType { get; }
}
