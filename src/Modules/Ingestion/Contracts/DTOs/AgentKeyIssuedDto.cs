namespace Liakont.Modules.Ingestion.Contracts.DTOs;

/// <summary>
/// Résultat de l'émission d'une clé d'agent (enregistrement ou rotation). La clé complète
/// (<see cref="FullKey"/>) n'est renvoyée qu'ICI, UNE seule fois (F12 §4.2) : elle n'est jamais
/// persistée en clair ni relisible ensuite. L'opérateur doit la transmettre à l'agent immédiatement.
/// </summary>
public sealed record AgentKeyIssuedDto
{
    public required Guid AgentId { get; init; }

    /// <summary>Préfixe public de la clé (identifie la clé sans permettre de s'authentifier).</summary>
    public required string KeyPrefix { get; init; }

    /// <summary>Clé API complète <c>prefix.secret</c> — à afficher une seule fois, jamais stockée.</summary>
    public required string FullKey { get; init; }
}
