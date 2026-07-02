namespace Liakont.Agent.Contracts.Ged;

using System;
using System.Collections.Generic;

/// <summary>
/// Indice d'axe BRUT observé par l'agent dans la source (F19 §4.2) — DTO PUR. L'agent DÉCLARE un
/// chemin/nom d'axe et ses valeurs telles quelles ; il ne mappe RIEN sur un axe cible (aucune
/// interprétation métier, CLAUDE.md n°6). Le mapping déclaratif <c>documentType → axe cible</c> vit
/// sur la PLATEFORME (§4.5, <c>GedMapper</c>), jamais côté agent.
/// </summary>
public sealed class RawAxisHint
{
    /// <summary>Crée un indice d'axe brut.</summary>
    /// <param name="name">Chemin/nom BRUT de l'axe tel que déclaré par la source (jamais un axe cible plateforme).</param>
    /// <param name="values">Valeurs BRUTES de l'axe (chaînes, non interprétées) ; jamais nul (coalescé en liste vide).</param>
    public RawAxisHint(string name, IReadOnlyList<string>? values)
    {
        Name = name;
        Values = values ?? Array.Empty<string>();
    }

    /// <summary>Chemin/nom BRUT de l'axe tel que déclaré par la source.</summary>
    public string Name { get; }

    /// <summary>Valeurs BRUTES de l'axe (chaînes non interprétées).</summary>
    public IReadOnlyList<string> Values { get; }
}
