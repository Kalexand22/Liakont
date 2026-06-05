namespace Liakont.Modules.TvaMapping.Contracts.Services;

using System.Collections.Generic;

/// <summary>
/// Requête de mapping pour UNE part de ligne : code régime source BRUT (issu de
/// <c>PivotLineDto.SourceRegimeCodes</c>), <see cref="Part"/> FOURNIE par l'appelant (jamais dérivée par
/// le service), et flags source effectifs. L'appelant (PIP01b, CHECK) construit ces requêtes depuis le
/// pivot — ADR-0004 D3-1 : une ligne peut porter PLUSIEURS codes régime, chacun donnant une requête (le
/// moteur peut scinder en plusieurs lignes pivot, BG-30). <see cref="LineRef"/> permet de re-rattacher le
/// résultat à la ligne d'origine.
/// </summary>
public sealed record TvaLineMappingRequest
{
    /// <summary>Code du régime TVA dans le système source (brut, propre à chaque logiciel).</summary>
    public required string SourceRegimeCode { get; init; }

    /// <summary>Part de la ligne (fournie par l'appelant — F03 §4.1 ; jamais devinée par le service).</summary>
    public required TvaMappingPart Part { get; init; }

    /// <summary>Flags source EFFECTIFS du document (nom → valeur), <c>null</c> si le document n'en porte aucun.</summary>
    public IReadOnlyDictionary<string, string>? SourceFlags { get; init; }

    /// <summary>Référence libre de la ligne d'origine (re-rattachement du résultat), facultative.</summary>
    public string? LineRef { get; init; }
}
