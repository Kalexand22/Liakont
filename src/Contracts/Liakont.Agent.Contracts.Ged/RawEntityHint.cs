namespace Liakont.Agent.Contracts.Ged;

/// <summary>
/// Indice d'entité BRUT observé par l'agent dans la source (F19 §4.2) — DTO PUR. L'agent DÉCLARE un
/// type d'entité, son identifiant externe et un libellé d'affichage tels quels ; il ne résout AUCUNE
/// identité (aucune déduplication, aucune fusion — CLAUDE.md n°6). La résolution d'identité canonique
/// (§4.4) et le mapping <c>type source → EntityType cible</c> (§4.5) vivent sur la PLATEFORME.
/// Symétrie pivot « champ absent → <c>null</c> » : le libellé est optionnel.
/// </summary>
public sealed class RawEntityHint
{
    /// <summary>Crée un indice d'entité brut.</summary>
    /// <param name="type">Type d'entité BRUT tel que déclaré par la source (jamais un EntityType cible plateforme).</param>
    /// <param name="externalId">Identifiant externe BRUT de l'entité dans la source (clé de réconciliation).</param>
    /// <param name="display">Libellé d'affichage BRUT, ou <c>null</c> si la source n'en fournit pas.</param>
    public RawEntityHint(string type, string externalId, string? display = null)
    {
        Type = type;
        ExternalId = externalId;
        Display = display;
    }

    /// <summary>Type d'entité BRUT tel que déclaré par la source.</summary>
    public string Type { get; }

    /// <summary>Identifiant externe BRUT de l'entité dans la source.</summary>
    public string ExternalId { get; }

    /// <summary>Libellé d'affichage BRUT (<c>null</c> si absent).</summary>
    public string? Display { get; }
}
