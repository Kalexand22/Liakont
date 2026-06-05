namespace Liakont.Agent.Core.Extraction;

/// <summary>
/// Forme de la clé de régime TVA portée par les lignes de la source (capacité déclarée — ADR-0004 D2).
/// L'agent ne fait que DÉCLARER la forme observée ; le mapping et l'interprétation vivent sur la
/// plateforme (CLAUDE.md n°2). La plateforme s'adapte à la capacité, jamais par <c>if (source is NAV)</c>.
/// </summary>
public enum RegimeKeyShape
{
    /// <summary>Un seul code régime par ligne (cas le plus simple).</summary>
    Simple = 1,

    /// <summary>Un couple de codes recouvre le régime d'une ligne (ex. posting groups NAV).</summary>
    Composite = 2,

    /// <summary>Plusieurs taxes sur une même ligne (ex. ligne multi-taxes Axelor).</summary>
    MultiplePerLine = 3,
}
