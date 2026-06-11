namespace Liakont.Modules.TvaMapping.Contracts.Queries;

using Liakont.Modules.TvaMapping.Contracts.DTOs;
using Stratum.Common.Abstractions.Messaging;

/// <summary>
/// Calcule le rapport de cohérence du paramétrage de mapping TVA du tenant courant (lot FIX03) :
/// confronte les règles de la table aux parts consultées par le pipeline du tenant et aux régimes
/// observés, pour signaler les règles mortes (part non consultée, code jamais observé). Tenant-scopée
/// par le contexte appelant (CLAUDE.md n°9/17). Recalculée à la demande (toujours à jour après chaque
/// modification de la table ou push d'agent).
///
/// L'activation du vertical enchères est FOURNIE par l'appelant (<see cref="AuctionVerticalEnabled"/>) :
/// le module TvaMapping ne lit jamais le paramétrage d'un autre module — même convention que la
/// fourniture de la part au moteur (TvaMappingPart). C'est elle qui détermine les parts consultées
/// (voir <c>ConsultedMappingParts</c>) ; aucune règle fiscale n'est inventée.
/// </summary>
public sealed record GetMappingConsistencyReportQuery : IQuery<MappingConsistencyReportDto>
{
    /// <summary><c>true</c> si le vertical enchères est activé pour le tenant (paramétrage produit D4).</summary>
    public required bool AuctionVerticalEnabled { get; init; }
}
