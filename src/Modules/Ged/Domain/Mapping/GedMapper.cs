namespace Liakont.Modules.Ged.Domain.Mapping;

using System.Collections.Generic;
using Liakont.Agent.Contracts.Ged;
using Liakont.Modules.Ged.Domain.Catalog;

/// <summary>
/// Moteur de mapping déclaratif GED (F19 §4.5, INV-GED-05 ; généralisation de <c>TvaMapper</c>). PUR : à partir
/// d'un <see cref="GedMappingProfile"/> VALIDÉ et d'un <c>IngestedDocumentDto</c> BRUT, produit soit un
/// <see cref="MappedDocument"/>, soit un <b>déférement</b> (<see cref="GedMappingResult"/>) — jamais une valeur
/// inventée (règle 2), jamais un rejet silencieux (règle 3). Comme <c>TvaMapper</c>, ne LÈVE PAS pour un cas non
/// mappable : le déférement est un résultat métier, pas une erreur.
/// <para>
/// Cas de DÉFÉREMENT (INV-GED-05) : profil absent ou non validé ; type de document non couvert ; axe inconnu du
/// catalogue ; axe <b>obligatoire</b> non résolu ; axe <b>mono-valeur</b> dont le sélecteur est ambigu (&gt; 1
/// valeur) ; valeur source incompatible avec le <c>data_type</c> de l'axe (parsing ambigu, ex. date non ISO).
/// </para>
/// </summary>
public static class GedMapper
{
    /// <summary>
    /// Mappe un document ingéré contre un profil tenant VALIDÉ. Rend un <see cref="MappedDocument"/> (mappé) ou
    /// un déférement motivé (français).
    /// </summary>
    /// <param name="profile">Le profil du <c>documentType</c>, ou <see langword="null"/> si aucun n'existe.</param>
    /// <param name="ingested">Le document ingéré BRUT.</param>
    /// <param name="catalog">Le catalogue d'axes tenant (résolution type/échelle pour la normalisation).</param>
    /// <returns>Le résultat de mapping (mappé ou déféré).</returns>
    public static GedMappingResult Map(
        GedMappingProfile? profile,
        IngestedDocumentDto ingested,
        IAxisMappingCatalog catalog)
    {
        System.ArgumentNullException.ThrowIfNull(ingested);
        System.ArgumentNullException.ThrowIfNull(catalog);

        if (profile is null)
        {
            return GedMappingResult.Deferred(
                $"Aucun profil de mapping n'existe pour le type de document « {ingested.DocumentType} » : document rangé en attente (deferred). Action opérateur : créer et valider un profil pour ce type.");
        }

        if (!profile.IsValidated)
        {
            return GedMappingResult.Deferred(
                $"Le profil de mapping du type de document « {profile.DocumentType} » n'est pas validé : document rangé en attente (deferred). Action opérateur : valider le profil en console.");
        }

        if (!string.Equals(profile.DocumentType, ingested.DocumentType, System.StringComparison.Ordinal))
        {
            return GedMappingResult.Deferred(
                $"Le profil fourni (« {profile.DocumentType} ») ne correspond pas au type du document ingéré (« {ingested.DocumentType} ») : document rangé en attente (deferred).");
        }

        var axes = new List<MappedAxisValue>();
        foreach (var rule in profile.AxisRules)
        {
            var target = catalog.Resolve(rule.AxisCode);
            if (target is null)
            {
                return GedMappingResult.Deferred(
                    $"Le document « {ingested.SourceReference} » réfère un axe « {rule.AxisCode} » inconnu ou inactif du catalogue : document rangé en attente (deferred). Action opérateur : déclarer/activer l'axe.");
            }

            var values = GedSelector.Evaluate(rule.Source, ingested);
            if (values.Count == 0)
            {
                if (rule.IsRequired)
                {
                    return GedMappingResult.Deferred(
                        $"Le document « {ingested.SourceReference} » n'a pas de valeur pour l'axe OBLIGATOIRE « {rule.AxisCode} » (sélecteur « {rule.Source} ») : document rangé en attente (deferred).");
                }

                continue;
            }

            if (values.Count > 1 && !rule.IsMulti)
            {
                return GedMappingResult.Deferred(
                    $"L'axe mono-valeur « {rule.AxisCode} » du document « {ingested.SourceReference} » est ambigu : le sélecteur « {rule.Source} » a renvoyé {values.Count} valeurs. Document rangé en attente (deferred) — jamais deviner laquelle retenir.");
            }

            // Recoupement cardinalité profil↔catalogue (INV-GED-05, règle 3) : un profil déclaré MULTI sur un axe
            // que le CATALOGUE déclare MONO ne peut ranger plusieurs valeurs — le chemin d'écriture supersède la
            // courante (RL-02, dernier gagnant arbitraire, PostgresGedIndexUnitOfWork) et perd les précédentes EN
            // SILENCE. On DÉFÈRE plutôt que d'écraser (jamais deviner laquelle garder) ; une valeur unique (ou zéro)
            // passe sans risque. La décision est prise ICI (au mapping, où le catalogue est résolu) plutôt qu'à
            // l'enregistrement du profil : le mapper est le point où la cardinalité effective du catalogue est connue.
            if (values.Count > 1 && !target.IsMultiValue)
            {
                return GedMappingResult.Deferred(
                    $"L'axe « {rule.AxisCode} » du document « {ingested.SourceReference} » est déclaré MULTI-valeur par le profil mais MONO-valeur par le catalogue : le sélecteur « {rule.Source} » a renvoyé {values.Count} valeurs qui ne peuvent être rangées sans écrasement silencieux. Document rangé en attente (deferred). Action opérateur : rendre l'axe multi-valeur au catalogue, ou mono-valeur au profil.");
            }

            // Dédup avant append (V009 n'impose aucune contrainte d'unicité ; le sélecteur conserve les doublons du
            // document) : deux valeurs produisant le MÊME lien courant ne créent qu'UN lien — sinon facette/fiche en
            // double, jamais corrigeables (index append-only, règle 4). L'identité d'un lien courant est la valeur
            // NORMALISÉE (clé de facette/recherche : le SQL de facettes groupe par normalized_value), donc deux valeurs
            // ne différant que par la casse/les espaces (« Paris »/« PARIS ») partagent la même valeur normalisée →
            // un seul lien. Un axe json n'a PAS de valeur normalisée (présentation-only, INV-GED-04) → repli sur
            // l'égalité complète du record pour ne pas fusionner à tort des fragments json distincts.
            var seenNormalized = new HashSet<string>(System.StringComparer.Ordinal);
            var seenJsonRecords = new HashSet<NormalizedAxisValue>();
            foreach (var raw in values)
            {
                NormalizedAxisValue normalized;
                try
                {
                    normalized = ValueNormalizer.Normalize(target.DataType, target.ValueScale, raw);
                }
                catch (AxisValueFormatException ex)
                {
                    return GedMappingResult.Deferred(
                        $"La valeur « {raw} » de l'axe « {rule.AxisCode} » du document « {ingested.SourceReference} » est incompatible avec son type déclaré : {ex.Message} Document rangé en attente (deferred) — jamais interpréter au mieux.");
                }

                var isNovel = normalized.NormalizedValue is { } normalizedKey
                    ? seenNormalized.Add(normalizedKey)
                    : seenJsonRecords.Add(normalized);
                if (isNovel)
                {
                    axes.Add(new MappedAxisValue(rule.AxisCode, normalized));
                }
            }
        }

        var entities = MapEntities(profile, ingested);
        var relations = MapRelations(profile, ingested);

        return GedMappingResult.Mapped(
            new MappedDocument(ingested.DocumentType, ingested.SourceReference, axes, entities, relations));
    }

    private static List<MappedEntity> MapEntities(GedMappingProfile profile, IngestedDocumentDto ingested)
    {
        var entities = new List<MappedEntity>();
        foreach (var rule in profile.EntityRules)
        {
            // Appariement du libellé à l'identifiant À LA SOURCE (sur le MÊME nœud) : identifiant et libellé sont
            // deux sélecteurs INDÉPENDANTS et le sélecteur saute les valeurs nulles, donc deux listes scalaires
            // évaluées séparément se compactent séparément — une égalité de décompte ne garantit PAS l'alignement
            // (un libellé pourrait être collé à une AUTRE entité, INV-GED-05 n°3). La jointure par nœud parent
            // garantit que chaque identifiant reçoit le libellé de SA propre entité, ou null (best-effort : une
            // entité sans identifiant externe ne produit aucun lien ; un identifiant sans libellé reste sans libellé).
            foreach (var (externalId, display) in GedSelector.EvaluatePaired(rule.ExternalIdSource, rule.DisplaySource, ingested))
            {
                entities.Add(new MappedEntity(rule.EntityType, externalId, display));
            }
        }

        return entities;
    }

    private static List<MappedRelation> MapRelations(GedMappingProfile profile, IngestedDocumentDto ingested)
    {
        var relations = new List<MappedRelation>();
        foreach (var rule in profile.RelationRules)
        {
            var targets = GedSelector.Evaluate(rule.TargetExternalIdSource, ingested);
            foreach (var targetExternalId in targets)
            {
                relations.Add(new MappedRelation(rule.Kind, rule.TargetType, targetExternalId));
            }
        }

        return relations;
    }
}
