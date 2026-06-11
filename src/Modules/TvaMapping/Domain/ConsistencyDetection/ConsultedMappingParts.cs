namespace Liakont.Modules.TvaMapping.Domain.ConsistencyDetection;

using Liakont.Modules.TvaMapping.Domain.Entities;

/// <summary>
/// Ensemble des parts de mapping réellement consultées par le pipeline générique du tenant — base du
/// contrôle de cohérence (lot FIX03).
///
/// Le CHECK mappe TOUJOURS avec la part <c>Autre</c> (<c>CheckTvaMapping.LinePart</c>) : la dérivation
/// adjudication/frais depuis une ligne pivot est une décision fiscale OUVERTE et figée (ADR-0004 / F03
/// §2.3, item PIP03b gelé). Tant que cette dérivation n'est pas livrée, AUCUN document n'est mappé avec
/// la part Adjudication ou Frais — <b>indépendamment de l'activation du vertical enchères</b>, qui ne
/// gouverne QUE l'exposition du champ « part » dans l'éditeur (décision D4). Une règle Adjudication/Frais
/// est donc morte (jamais consultée) y compris vertical activé : le contrôle de cohérence reflète cette
/// RÉALITÉ du pipeline, pas l'intention déclarée — sinon il affirmerait opérante une règle qui ne
/// s'appliquera jamais (faux-négatif d'avertissement, contraire à CLAUDE.md n°3 « on en montre plus »).
///
/// Quand PIP03b livrera la dérivation, c'est ICI (seul point) que le jeu consulté deviendra
/// activation-dépendant.
/// </summary>
public static class ConsultedMappingParts
{
    /// <summary>Parts effectivement consultées par le pipeline générique : <c>{ Autre }</c> (voir résumé).</summary>
    public static IReadOnlySet<MappingPart> PipelineConsulted() => new HashSet<MappingPart> { MappingPart.Autre };
}
