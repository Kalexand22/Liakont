namespace Liakont.Modules.TvaMapping.Domain.ConsistencyDetection;

using Liakont.Modules.TvaMapping.Domain.Entities;

/// <summary>
/// Détermine l'ensemble des parts de mapping réellement consultées par le pipeline du tenant, à partir
/// de l'activation du vertical enchères (décision opérateur D4, lot FIX03).
///
/// Le pipeline générique mappe TOUJOURS avec la part <c>Autre</c> (CheckTvaMapping.LinePart — la
/// dérivation adjudication/frais reste une décision fiscale OUVERTE, ADR-0004 / F03 §2.3). Le découpage
/// Adjudication / Frais n'a de sens que lorsque le tenant a DÉCLARÉ opérer le vertical enchères : c'est
/// cette déclaration (activation) qui met ces parts « en scope ». Hors vertical, seule <c>Autre</c> est
/// consultée — une règle Adjudication / Frais y est morte. Dérivé de l'activation du tenant, AUCUNE règle
/// fiscale n'est inventée ici (CLAUDE.md n°2/8).
/// </summary>
public static class ConsultedMappingParts
{
    /// <summary>Parts consultées selon l'activation du vertical enchères.</summary>
    public static IReadOnlySet<MappingPart> For(bool auctionVerticalEnabled)
    {
        return auctionVerticalEnabled
            ? new HashSet<MappingPart> { MappingPart.Adjudication, MappingPart.Frais, MappingPart.Autre }
            : new HashSet<MappingPart> { MappingPart.Autre };
    }
}
