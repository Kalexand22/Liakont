namespace Liakont.Host.Ged;

/// <summary>
/// Une facette de recherche GED (projetée depuis <c>SearchFacet</c>, GED08) : pour un axe <c>is_facetable</c>, une
/// valeur et le nombre de documents la portant. Les facettes sur un axe confidentiel sont EXCLUES côté serveur sans
/// le droit <c>liakont.ged.confidential</c> (anti-oracle §6.5) — la page ne reçoit donc jamais un compte à masquer.
/// </summary>
/// <param name="AxisCode">Code machine de l'axe facetté.</param>
/// <param name="Value">Valeur d'axe proposée comme filtre.</param>
/// <param name="Count">Nombre de documents portant cette valeur (dans le périmètre courant).</param>
public sealed record GedSearchFacet(string AxisCode, string Value, long Count);
