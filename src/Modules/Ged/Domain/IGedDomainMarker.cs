namespace Liakont.Modules.Ged.Domain;

/// <summary>
/// Marqueur d'assembly du Domain GED. Y atterrissent à partir de GED03+ le méta-modèle générique
/// (<c>EntityType</c> polymorphe, <c>ValueNormalizer</c> — axe <c>number</c> en decimal, arrondi half-up,
/// F19 §7 règle 1) et la logique pure RE-COPIÉE <c>GedIngestionDecision</c> (RL-01) — SANS aucune dépendance
/// au flux fiscal (F19 §7 frontière). Homogène avec les autres modules (scaffold GED02).
/// </summary>
public interface IGedDomainMarker;
