namespace Liakont.Modules.TvaMapping.Domain.Services;

/// <summary>
/// Entrée du catalogue VATEX (<see cref="VatexCatalog"/>) : un code (transmis à l'administration) et
/// son usage lisible (affichage console). Transcrit de <c>docs/conception/F03-Mapping-TVA.md §2.2</c> ;
/// aucune interprétation fiscale n'est ajoutée (CLAUDE.md n°2).
/// </summary>
/// <param name="Code">Code VATEX (ex. <c>VATEX-EU-J</c>).</param>
/// <param name="Description">Usage lisible, transcrit de la colonne « Usage » de F03 §2.2.</param>
public sealed record VatexCatalogEntry(string Code, string Description);
