namespace Liakont.Modules.Ged.Web;

/// <summary>
/// Marqueur d'assembly de la couche Web GED. Le portail (pages <c>/ged/recherche</c>, <c>/ged/document/{id}</c>,
/// <c>/ged/objet/{type}/{id}</c>) atterrit avec GED09a/b/c — pages MINCES hébergées dans <c>Liakont.Host</c>,
/// vues-pures testées bUnit, AUCUNE logique métier en page (déléguée aux handlers MediatR ; F19 §6.7,
/// module-rules §19). Homogène avec les autres modules (scaffold GED02).
/// </summary>
public interface IGedWebMarker;
