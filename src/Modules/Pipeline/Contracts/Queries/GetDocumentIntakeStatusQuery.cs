namespace Liakont.Modules.Pipeline.Contracts.Queries;

using Liakont.Agent.Contracts.Transport;
using Stratum.Common.Abstractions.Messaging;

/// <summary>
/// Point de statut agent (ADR-0012/0014, PIP01d) : résout la prise en charge d'un document poussé,
/// identifié par <c>(sourceReference, payloadHash)</c>, en lecture seule et TENANT-SCOPÉE (la requête
/// s'exécute sur la base DU TENANT courant). Sémantique terminale définie par le pipeline :
/// <list type="bullet">
///   <item><see cref="DocumentIntakeStatus.Processed"/> = un Document existe pour la clé (Detected ET AU-DELÀ,
///   Issued inclus — invariant happened-before : le staging a été écrit AVANT l'événement ; un Issued, dont le
///   staging est purgé, répond TOUJOURS Processed, jamais Pending) ;</item>
///   <item><see cref="DocumentIntakeStatus.Pending"/> = clé inconnue / reçue mais pas encore rangée (l'agent
///   renvoie l'élément). Une clé inconnue sur cette route EXISTANTE répond 200 + Pending, JAMAIS 404.</item>
/// </list>
/// La valeur <see cref="DocumentIntakeStatus.Rejected"/> n'est jamais retournée par ce point : un rejet d'intake
/// (payload non conforme) n'est rien persisté → la clé reste inconnue → Pending ; l'agent apprend un rejet par le
/// 400 SYNCHRONE au push. La valeur reste dans le DTO pour compatibilité ascendante.
/// </summary>
public sealed record GetDocumentIntakeStatusQuery : IQuery<DocumentStatusResultDto>
{
    /// <summary>Référence du document dans le système source (clé de réconciliation).</summary>
    public required string SourceReference { get; init; }

    /// <summary>Empreinte canonique du payload (SHA-256 hex) — clé d'idempotence partagée avec la file agent.</summary>
    public required string PayloadHash { get; init; }
}
