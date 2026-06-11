namespace Liakont.Modules.Documents.Contracts.DTOs;

using System;

/// <summary>
/// Entrée de la piste d'audit d'un document exposée en lecture (item TRK01). Reflète l'immuabilité du
/// journal côté base : les consommateurs n'en lisent que l'historique, jamais une mutation.
/// </summary>
public sealed record DocumentEventDto
{
    public required Guid Id { get; init; }

    public required Guid DocumentId { get; init; }

    public required DateTimeOffset TimestampUtc { get; init; }

    public required string EventType { get; init; }

    public string? Detail { get; init; }

    public string? PayloadSnapshot { get; init; }

    public string? PaResponseSnapshot { get; init; }

    public string? MappingTrace { get; init; }

    public string? OperatorIdentity { get; init; }

    /// <summary>
    /// Nom d'affichage de l'opérateur capturé au moment de l'événement (item FIX305) : la restitution
    /// affiche ce nom, l'<see cref="OperatorIdentity"/> (GUID) restant le détail technique. <c>null</c>
    /// pour un événement système ou antérieur à FIX305 (repli sur le GUID).
    /// </summary>
    public string? OperatorName { get; init; }
}
