namespace Liakont.Modules.Ged.Contracts;

/// <summary>
/// Marqueur d'assembly de la surface publique du module GED (SEULE surface visible des autres modules —
/// module-rules §3). Les commandes/requêtes/DTO et l'événement d'intégration GED
/// (<c>ManagedDocumentReceivedV1</c>, F19 §2.2) atterrissent ici à partir de GED03+/GED05b. Homogène avec
/// les autres modules (scaffold GED02).
/// </summary>
public interface IGedContractsMarker;
