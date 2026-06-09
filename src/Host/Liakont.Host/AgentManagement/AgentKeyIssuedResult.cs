namespace Liakont.Host.AgentManagement;

using Liakont.Modules.Ingestion.Contracts.DTOs;

/// <summary>
/// Résultat d'une émission de clé (enregistrement / rotation) : le statut, en cas de succès la clé COMPLÈTE
/// renvoyée UNE seule fois (<see cref="AgentKeyIssuedDto.FullKey"/> — jamais persistée ni relisible, F12 §4.2),
/// et, sur <see cref="AgentActionStatus.Conflict"/>, le message d'erreur du domaine porté tel quel à
/// l'opérateur (parité avec le corps 409 de l'endpoint API05).
/// </summary>
public sealed record AgentKeyIssuedResult(AgentActionStatus Status, AgentKeyIssuedDto? IssuedKey, string? ErrorMessage = null);
