namespace Liakont.Modules.TvaMapping.Domain.Entities;

/// <summary>
/// Nature d'une modification journalisée de la table de mapping TVA (item TVA05 §3). Chaque mutation
/// produit une entrée immuable de <c>MappingChangeLog</c> (append-only) qui en porte le type, l'auteur
/// et la valeur avant/après — la piste permet de prouver, des années après, qui a changé quoi.
/// </summary>
public enum MappingChangeType
{
    /// <summary>Ajout d'une règle de mapping.</summary>
    AddRule = 0,

    /// <summary>Modification d'une règle existante (identifiée par code régime + part).</summary>
    UpdateRule = 1,

    /// <summary>Suppression d'une règle existante.</summary>
    RemoveRule = 2,

    /// <summary>Validation humaine de la table (workflow expert-comptable, item TVA05 §4).</summary>
    Validate = 3,
}
