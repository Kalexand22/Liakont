namespace Liakont.Modules.Ged.Domain.Mapping;

using System.Collections.Generic;

/// <summary>
/// Résultat POSITIF d'un mapping (F19 §4.5) : le pivot GED indexable produit par <see cref="GedMapper"/> à
/// partir d'un profil VALIDÉ et d'un document ingéré. Consommé par l'ingestion (GED05b) pour écrire les liens
/// d'axe / d'entité / de relation. L'alternative est le DÉFÉREMENT (voir <see cref="GedMappingResult"/>).
/// </summary>
/// <param name="DocumentType">Type de document source (écho du document ingéré).</param>
/// <param name="SourceReference">Clé de réconciliation source (écho).</param>
/// <param name="Axes">Valeurs d'axe normalisées.</param>
/// <param name="Entities">Entités à rattacher.</param>
/// <param name="Relations">Relations à créer.</param>
public sealed record MappedDocument(
    string DocumentType,
    string SourceReference,
    IReadOnlyList<MappedAxisValue> Axes,
    IReadOnlyList<MappedEntity> Entities,
    IReadOnlyList<MappedRelation> Relations);
