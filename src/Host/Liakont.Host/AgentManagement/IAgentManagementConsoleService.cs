namespace Liakont.Host.AgentManagement;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Service d'assemblage de l'écran « Gestion des agents » (WEB09). LECTURE du parc (registre système,
/// tenant-scopé) + ACTIONS de cycle de vie (enregistrement, révocation, rotation) déléguées aux handlers
/// PIV05 (commandes MediatR) — AUCUNE logique métier ici (génération / hachage de clé, machine à états :
/// du ressort du domaine, CLAUDE.md n°3/19). Parité d'audit avec les endpoints API05 (l'action opérateur
/// est journalisée par le service car la console dispatche en in-process — voir l'implémentation).
/// </summary>
public interface IAgentManagementConsoleService
{
    /// <summary>Liste les agents du tenant courant (sans clé), avec l'indicateur « muet » calculé.</summary>
    Task<IReadOnlyList<AgentConsoleLine>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Enregistre un nouvel agent et émet sa clé (affichée une seule fois). Nom vide = refus.</summary>
    Task<AgentKeyIssuedResult> RegisterAsync(string? name, CancellationToken cancellationToken = default);

    /// <summary>Révoque un agent : sa clé est immédiatement refusée à l'ingestion.</summary>
    Task<AgentActionStatus> RevokeAsync(Guid agentId, CancellationToken cancellationToken = default);

    /// <summary>Renouvelle la clé d'un agent : l'ancienne est invalidée immédiatement, la nouvelle est émise.</summary>
    Task<AgentKeyIssuedResult> RotateKeyAsync(Guid agentId, CancellationToken cancellationToken = default);
}
