namespace Liakont.Modules.Documents.Contracts.DTOs;

using System;

/// <summary>
/// Statut minimal d'un document identifié par (référence source, empreinte du payload) — F06, brique du
/// point de statut agent (ADR-0012/0014, élaboré par PIP01d). Porte l'état DURABLE du document (Detected
/// et au-delà, Issued inclus). La requête retourne <c>null</c> quand aucun document n'existe pour la clé
/// (l'appelant le traite comme « pas encore reçu / Pending », jamais comme terminal).
/// </summary>
public sealed record DocumentStatusDto
{
    /// <summary>Identifiant du document.</summary>
    public required Guid Id { get; init; }

    /// <summary>Numéro du document (EN 16931 BT-1).</summary>
    public required string DocumentNumber { get; init; }

    /// <summary>État durable du document dans la passerelle (nom de <c>DocumentState</c>).</summary>
    public required string State { get; init; }
}
