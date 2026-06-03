namespace Liakont.Agent.Core;

/// <summary>
/// Contrat d'un extracteur de source legacy (plug-in côté agent). Chaque adaptateur
/// (EncheresV6, puis AS400, Sage...) implémente cette interface. L'extraction réelle
/// (lecture ODBC seule, mapping vers le pivot) arrive avec les items ADP : ici, seule
/// la frontière existe (blueprint.md §6 — l'agent n'a aucune logique métier).
/// </summary>
public interface IExtractor
{
    /// <summary>Nom de la source extraite (identifiant du plug-in, ex. « EncheresV6 »).</summary>
    string SourceName { get; }
}
