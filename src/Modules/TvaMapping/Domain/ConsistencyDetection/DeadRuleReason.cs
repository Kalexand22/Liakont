namespace Liakont.Modules.TvaMapping.Domain.ConsistencyDetection;

/// <summary>
/// Motif pour lequel une règle de mapping TVA est « morte » — c.-à-d. ne pourra jamais s'appliquer au
/// pipeline du tenant (contrôle de cohérence, lot FIX03, complément du rapport de couverture TVA03).
/// Un motif est un SIGNAL non bloquant (CLAUDE.md n°3 : on en montre plus, pas moins), jamais une règle
/// fiscale inventée.
/// </summary>
public enum DeadRuleReason
{
    /// <summary>
    /// La part de la règle (Adjudication / Frais) n'est consultée par AUCUN document : le pipeline
    /// générique mappe toujours avec la part <c>Autre</c> (<c>CheckTvaMapping.LinePart</c>) — la
    /// dérivation adjudication/frais est figée (ADR-0004 / PIP03b). Indépendant de l'activation du
    /// vertical enchères (qui ne gouverne que l'éditeur — D4).
    /// </summary>
    PartNotConsulted = 0,

    /// <summary>
    /// Le code régime de la règle n'a jamais été observé dans la source du tenant (faute de frappe
    /// probable) — la règle ne matchera aucun document. N'est signalé que si des régimes ONT été observés.
    /// </summary>
    RegimeNeverObserved = 1,
}
