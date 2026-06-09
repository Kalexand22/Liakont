namespace Liakont.Host.AgentManagement;

using Liakont.Modules.Ingestion.Contracts.DTOs;

/// <summary>
/// Résultat d'une émission de clé (enregistrement / rotation) : le statut et, en cas de succès, la clé
/// COMPLÈTE renvoyée UNE seule fois (<see cref="AgentKeyIssuedDto.FullKey"/>). La clé n'est portée que par
/// CE résultat transitoire — jamais persistée ni relisible (F12 §4.2).
/// </summary>
public sealed record AgentKeyIssuedResult(AgentActionStatus Status, AgentKeyIssuedDto? IssuedKey);
