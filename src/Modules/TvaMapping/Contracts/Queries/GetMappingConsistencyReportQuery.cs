namespace Liakont.Modules.TvaMapping.Contracts.Queries;

using Liakont.Modules.TvaMapping.Contracts.DTOs;
using Stratum.Common.Abstractions.Messaging;

/// <summary>
/// Calcule le rapport de cohérence du paramétrage de mapping TVA du tenant courant (lot FIX03) :
/// confronte les règles de la table aux parts réellement consultées par le pipeline du tenant et aux
/// régimes observés, pour signaler les règles mortes (part non consultée, code jamais observé).
/// Tenant-scopée par le contexte appelant (CLAUDE.md n°9/17). Recalculée à la demande (toujours à jour
/// après chaque modification de la table ou push d'agent).
///
/// Les parts consultées reflètent la RÉALITÉ du pipeline (toujours <c>{Autre}</c> tant que la dérivation
/// adjudication/frais — ADR-0004 / PIP03b — n'est pas livrée), pas l'activation du vertical enchères :
/// celle-ci ne gouverne que l'exposition du champ « part » dans l'éditeur (D4). Voir
/// <c>ConsultedMappingParts</c>. Aucune règle fiscale n'est inventée.
/// </summary>
public sealed record GetMappingConsistencyReportQuery : IQuery<MappingConsistencyReportDto>;
