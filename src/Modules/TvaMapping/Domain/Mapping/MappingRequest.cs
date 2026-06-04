namespace Liakont.Modules.TvaMapping.Domain.Mapping;

using Liakont.Modules.TvaMapping.Domain.Entities;

/// <summary>
/// Entrée du moteur de mapping TVA pour UNE part de ligne (item TVA02 §1, F03 §3/§4). Le régime TVA
/// source est BRUT (l'agent ne l'interprète jamais — CLAUDE.md n°2) et arrive via le pivot
/// (<see cref="Liakont.Agent.Contracts.Pivot.PivotLineDto.SourceRegimeCodes"/>) ; la part distingue
/// l'adjudication des frais (modèle du régime de la marge, F03 §2.3) ; les flags source effectifs du
/// document permettent de lever l'ambiguïté d'un même code (F03 §3 : <c>RegimeMarge</c> /
/// <c>assujetti_tva</c>). Aucune valeur n'est devinée ici : le moteur applique strictement la table
/// validée du tenant (<see cref="Services.TvaMapper"/>).
/// </summary>
public sealed record MappingRequest
{
    /// <summary>Code du régime TVA dans le système source (brut, propre à chaque logiciel).</summary>
    public required string SourceRegimeCode { get; init; }

    /// <summary>Part de la ligne concernée (adjudication / frais / autre).</summary>
    public required MappingPart Part { get; init; }

    /// <summary>
    /// Flags source EFFECTIFS du document (nom du flag → valeur observée), <c>null</c> si le document
    /// n'en porte aucun. Confrontés aux <see cref="MappingRule.SourceFlags"/> RESTRICTIFS de la règle :
    /// la règle ne s'applique que si tous ses flags requis sont satisfaits par ces valeurs (sinon le
    /// régime tombe sur le comportement par défaut <c>block</c> — item TVA01 §3, F03 §3). GÉNÉRIQUE :
    /// les noms de flags sont du paramétrage tenant, jamais codés en dur (CLAUDE.md n°7/INV-009).
    /// </summary>
    public IReadOnlyDictionary<string, string>? SourceFlags { get; init; }
}
