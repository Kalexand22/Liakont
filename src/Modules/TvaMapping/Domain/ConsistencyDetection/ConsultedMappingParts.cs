namespace Liakont.Modules.TvaMapping.Domain.ConsistencyDetection;

using Liakont.Modules.TvaMapping.Domain.Entities;

/// <summary>
/// Ensemble des parts de mapping réellement consultées par les CONSOMMATEURS du tenant — base du contrôle
/// de cohérence (lot FIX03). Source unique de vérité : on n'y déclare consultée qu'une part qu'un
/// consommateur RÉEL interroge (sinon faux-négatif d'avertissement, contraire à CLAUDE.md n°3).
///
/// Deux consommateurs lisent la table de mapping, chacun sur une part FIXE :
/// <list type="bullet">
///   <item><b>CHECK</b> (<c>CheckTvaMapping.LinePart</c>) mappe TOUJOURS la part <c>Autre</c> : la
///   dérivation adjudication/frais depuis une ligne pivot est une décision fiscale figée
///   (ADR-0004 / F03 §2.3, item PIP03b gelé).</item>
///   <item><b>B4 — e-reporting B2C de la marge</b> (<c>B2cMarginAggregatorTenantJob.ResolveMarginAsync</c>)
///   résout le taux des honoraires acheteur/vendeur en mappant la part <c>Frais</c> (F03 §2.4/§2.5).</item>
/// </list>
///
/// Le jeu consulté est donc <c>{ Autre, Frais }</c> — <b>indépendamment de l'activation du vertical
/// enchères</b>, qui ne gouverne QUE l'exposition du champ « composante » dans l'éditeur (décision D4).
/// Une règle <c>Frais</c> n'est ainsi JAMAIS marquée morte : B4 la consulte (corrige le faux « morte »
/// qui poussait à supprimer des règles Frais indispensables — BUG-3). Seule <c>Adjudication</c> reste
/// non consultée (aucun consommateur ne la lit) : le contrôle de cohérence la signale, à juste titre.
///
/// Quand PIP03b livrera la dérivation adjudication/frais au CHECK, c'est ICI (seul point) que
/// <c>Adjudication</c> rejoindra le jeu consulté.
/// </summary>
public static class ConsultedMappingParts
{
    /// <summary>
    /// Parts effectivement consultées par les consommateurs du tenant : <c>Autre</c> (CHECK) et
    /// <c>Frais</c> (B4 e-reporting B2C de la marge) — voir résumé. <c>Adjudication</c> reste exclue.
    /// </summary>
    public static IReadOnlySet<MappingPart> PipelineConsulted() =>
        new HashSet<MappingPart> { MappingPart.Autre, MappingPart.Frais };
}
