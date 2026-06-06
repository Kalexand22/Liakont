namespace Liakont.Modules.Pipeline.Infrastructure.Check;

using System.Collections.Generic;
using Liakont.Modules.TvaMapping.Contracts.Services;

/// <summary>
/// Plan de mapping d'un document au CHECK : les requêtes de ligne à soumettre au mapping (une par ligne de
/// forme non ambiguë, dans l'ordre des lignes), l'index de la ligne pivot d'origine de chaque requête, et
/// les motifs de blocage des lignes hors forme V1 (voir <see cref="CheckTvaMapping"/>).
/// </summary>
internal sealed record CheckMappingPlan
{
    /// <summary>Requêtes de mapping (une par ligne conforme), dans l'ordre des lignes pivot.</summary>
    public required IReadOnlyList<TvaLineMappingRequest> Requests { get; init; }

    /// <summary>Index de la ligne pivot d'origine pour chaque requête (<c>Requests[i]</c> → ligne <c>RequestLineIndexes[i]</c>).</summary>
    public required IReadOnlyList<int> RequestLineIndexes { get; init; }

    /// <summary>Motifs de blocage des lignes hors forme V1 (régimes/ventilations multiples).</summary>
    public required IReadOnlyList<string> ShapeBlockReasons { get; init; }
}
