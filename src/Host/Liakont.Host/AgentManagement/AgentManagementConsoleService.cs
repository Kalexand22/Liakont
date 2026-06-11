namespace Liakont.Host.AgentManagement;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Ingestion.Contracts.Commands;
using Liakont.Modules.Ingestion.Contracts.DTOs;
using Liakont.Modules.Ingestion.Contracts.Queries;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using MediatR;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Audit;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Common.Abstractions.Security;

/// <summary>
/// Implémentation de <see cref="IAgentManagementConsoleService"/> pour la page WEB09.
/// <para>
/// LECTURE : le parc du tenant courant via <see cref="IAgentQueries.ListByTenantAsync"/> (registre système,
/// scopé par <c>tenantId</c> — jamais cross-tenant, CLAUDE.md n°9), enrichi de l'indicateur « muet ».
/// L'agent est muet quand, NON révoqué, il n'a émis aucun heartbeat depuis plus que le seuil de supervision
/// (<c>AlertThresholdsDto.AgentSilentHours</c>, défaut F12 §5.2, surchargeable par tenant — CFG02) : MÊME
/// définition que la règle SUP01 <c>AgentMuteAlertRule</c> (référence = <c>LastSeenAtUtc ?? CreatedAt</c>),
/// aucune valeur inventée. L'horloge est injectée (<see cref="TimeProvider"/>) pour des tests déterministes.
/// </para>
/// <para>
/// ACTIONS : pass-through vers les commandes PIV05 (<see cref="RegisterAgentCommand"/>,
/// <see cref="RevokeAgentCommand"/>, <see cref="RotateAgentKeyCommand"/>) — le tenant est résolu côté handler
/// (jamais un paramètre client). PARITÉ D'AUDIT avec <c>AgentManagementEndpointMapping</c> (API05) : la console
/// dispatche EN IN-PROCESS (architecture établie des pages console — pas d'appel HTTP en boucle locale), et
/// comme les handlers PIV05 ne journalisent pas l'action OPÉRATEUR (l'audit vit à la frontière HTTP), le
/// service reproduit ici les mêmes entrées d'activité (<see cref="IActivityLogger"/>) — sinon une révocation /
/// rotation faite depuis la console n'aurait AUCUNE trace (piste d'audit, CLAUDE.md n°4). La clé complète n'est
/// renvoyée qu'au résultat d'émission, jamais journalisée (seul le préfixe public l'est — CLAUDE.md n°10).
/// </para>
/// </summary>
internal sealed partial class AgentManagementConsoleService : IAgentManagementConsoleService
{
    /// <summary>Type d'entité de la piste d'audit pour une opération de gestion d'agent (parité endpoint API05).</summary>
    private const string AgentEntityType = "Agent";

    /// <summary>
    /// Seuil par défaut produit (F12 §5.2), surchargé par <c>AlertThresholdsDto.AgentSilentHours</c>. Dupliqué
    /// ici (comme dans <c>AgentMuteAlertRule</c>) car la valeur du Domain TenantSettings n'est pas accessible
    /// hors de son module — la source de vérité reste F12 §5.2.
    /// </summary>
    private const int DefaultAgentSilentHours = 24;

    private readonly IAgentQueries _agentQueries;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantSettingsQueries _tenantSettings;
    private readonly ISender _sender;
    private readonly IActorContextAccessor _actorContext;
    private readonly IActivityLogger _activityLogger;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AgentManagementConsoleService> _logger;

    /// <summary>
    /// Constructeur d'injection : horloge SYSTÈME par défaut. <see cref="TimeProvider"/> n'est pas enregistré
    /// dans le conteneur (même motif que <c>ReconciliationService</c> / <c>AlertEvaluationService</c>) — le
    /// conteneur sélectionne donc ce constructeur ; les tests injectent une horloge figée via la surcharge.
    /// </summary>
    public AgentManagementConsoleService(
        IAgentQueries agentQueries,
        ITenantContext tenantContext,
        ITenantSettingsQueries tenantSettings,
        ISender sender,
        IActorContextAccessor actorContext,
        IActivityLogger activityLogger,
        ILogger<AgentManagementConsoleService> logger)
        : this(agentQueries, tenantContext, tenantSettings, sender, actorContext, activityLogger, TimeProvider.System, logger)
    {
    }

    public AgentManagementConsoleService(
        IAgentQueries agentQueries,
        ITenantContext tenantContext,
        ITenantSettingsQueries tenantSettings,
        ISender sender,
        IActorContextAccessor actorContext,
        IActivityLogger activityLogger,
        TimeProvider timeProvider,
        ILogger<AgentManagementConsoleService> logger)
    {
        _agentQueries = agentQueries;
        _tenantContext = tenantContext;
        _tenantSettings = tenantSettings;
        _sender = sender;
        _actorContext = actorContext;
        _activityLogger = activityLogger;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AgentConsoleLine>> ListAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId;
        if (string.IsNullOrEmpty(tenantId))
        {
            return [];
        }

        var agents = await _agentQueries.ListByTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (agents.Count == 0)
        {
            return [];
        }

        var silentLimit = TimeSpan.FromHours(await ResolveSilentHoursAsync(cancellationToken).ConfigureAwait(false));
        var nowUtc = _timeProvider.GetUtcNow();

        var lines = new List<AgentConsoleLine>(agents.Count);
        foreach (var agent in agents)
        {
            lines.Add(new AgentConsoleLine
            {
                Id = agent.Id,
                Name = agent.Name,
                KeyPrefix = agent.KeyPrefix,
                IsRevoked = agent.IsRevoked,
                IsSilent = IsSilent(agent, nowUtc, silentLimit),
                LastSeenUtc = agent.LastSeenAtUtc,
                Version = agent.LastAgentVersion,
                CreatedAt = agent.CreatedAt,
            });
        }

        return lines;
    }

    public async Task<AgentKeyIssuedResult> RegisterAsync(string? name, CancellationToken cancellationToken = default)
    {
        // Nom obligatoire validé AVANT tout dispatch (parité endpoint API05 → 400) : un nom vide retomberait
        // sinon en ArgumentException du domaine (chemin 500), jamais un message exploitable.
        if (string.IsNullOrWhiteSpace(name))
        {
            return new AgentKeyIssuedResult(AgentActionStatus.NameRequired, IssuedKey: null);
        }

        AgentKeyIssuedDto issued;
        try
        {
            issued = await _sender.Send(new RegisterAgentCommand { Name = name }, cancellationToken).ConfigureAwait(false);
        }
        catch (ConflictException ex)
        {
            // Conflit métier attendu (collision de préfixe de clé, F12 §4.2) : message du domaine porté tel
            // quel (parité 409 endpoint), pas une panne → on ne journalise pas en erreur.
            return new AgentKeyIssuedResult(AgentActionStatus.Conflict, IssuedKey: null, ex.Message);
        }
        catch (Exception ex)
        {
            LogRegisterFailed(_logger, ex);
            return new AgentKeyIssuedResult(AgentActionStatus.Failed, IssuedKey: null);
        }

        // L'agent est créé et sa clé émise (IRRÉVERSIBLE, affichée une seule fois) : la clé est TOUJOURS
        // retournée. L'audit ci-dessous est ISOLÉ et best-effort — un échec de journalisation ne doit jamais
        // faire perdre la clé unique ni convertir un succès irréversible en « Réessayez plus tard ».
        await AuditBestEffortAsync(
            issued.AgentId.ToString(),
            "agents.registered",
            string.Create(CultureInfo.InvariantCulture, $"Agent « {name} » enregistré par l'opérateur (préfixe de clé {issued.KeyPrefix})."),
            new { agentName = name, issued.KeyPrefix },
            cancellationToken).ConfigureAwait(false);

        return new AgentKeyIssuedResult(AgentActionStatus.Succeeded, issued);
    }

    public async Task<AgentActionStatus> RevokeAsync(Guid agentId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _sender.Send(new RevokeAgentCommand { AgentId = agentId }, cancellationToken).ConfigureAwait(false);
        }
        catch (NotFoundException)
        {
            // Agent inexistant dans le tenant courant (déjà retiré, ou course) — refus attendu, pas une panne.
            return AgentActionStatus.NotFound;
        }
        catch (Exception ex)
        {
            LogRevokeFailed(_logger, agentId, ex);
            return AgentActionStatus.Failed;
        }

        // L'agent est révoqué (IRRÉVERSIBLE) : un raté d'audit isolé ne convertit pas le succès en échec.
        await AuditBestEffortAsync(
            agentId.ToString(),
            "agents.revoked",
            "Agent révoqué par l'opérateur : sa clé est immédiatement refusée à l'ingestion.",
            metadata: null,
            cancellationToken).ConfigureAwait(false);

        return AgentActionStatus.Succeeded;
    }

    public async Task<AgentKeyIssuedResult> RotateKeyAsync(Guid agentId, CancellationToken cancellationToken = default)
    {
        AgentKeyIssuedDto issued;
        try
        {
            issued = await _sender.Send(new RotateAgentKeyCommand { AgentId = agentId }, cancellationToken).ConfigureAwait(false);
        }
        catch (NotFoundException)
        {
            return new AgentKeyIssuedResult(AgentActionStatus.NotFound, IssuedKey: null);
        }
        catch (ConflictException ex)
        {
            // Conflit métier attendu : rotation d'un agent révoqué (course « révocation entre l'affichage de
            // la liste et la rotation »). Message du domaine porté tel quel (parité 409 endpoint), pas une
            // panne → on ne journalise pas en erreur.
            return new AgentKeyIssuedResult(AgentActionStatus.Conflict, IssuedKey: null, ex.Message);
        }
        catch (Exception ex)
        {
            LogRotateFailed(_logger, agentId, ex);
            return new AgentKeyIssuedResult(AgentActionStatus.Failed, IssuedKey: null);
        }

        // La NOUVELLE clé est émise et l'ancienne DÉJÀ invalidée (IRRÉVERSIBLE) : la nouvelle clé est TOUJOURS
        // retournée pour son affichage unique — un échec d'audit isolé ne doit jamais « bricker » l'agent.
        await AuditBestEffortAsync(
            agentId.ToString(),
            "agents.key_rotated",
            string.Create(CultureInfo.InvariantCulture, $"Clé de l'agent renouvelée par l'opérateur (nouveau préfixe {issued.KeyPrefix}) : l'ancienne clé est immédiatement invalidée."),
            new { issued.KeyPrefix },
            cancellationToken).ConfigureAwait(false);

        return new AgentKeyIssuedResult(AgentActionStatus.Succeeded, issued);
    }

    /// <summary>
    /// Un agent NON révoqué est muet quand sa dernière référence d'activité (heartbeat, à défaut date
    /// d'enregistrement) est antérieure au seuil — exactement la condition de <c>AgentMuteAlertRule</c>.
    /// </summary>
    private static bool IsSilent(AgentSummaryDto agent, DateTimeOffset nowUtc, TimeSpan silentLimit)
    {
        if (agent.IsRevoked)
        {
            return false;
        }

        var reference = agent.LastSeenAtUtc ?? agent.CreatedAt;
        return nowUtc - reference > silentLimit;
    }

    /// <summary>Identité d'audit de l'opérateur (GUID utilisateur ; « system » si non authentifié — parité endpoint).</summary>
    private static string ActorId(IActorContext actor) =>
        actor.IsAuthenticated ? actor.UserId.ToString() : "system";

    [LoggerMessage(Level = LogLevel.Error, Message = "Agent registration failed from the console.")]
    private static partial void LogRegisterFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Agent revocation failed from the console for agent {AgentId}.")]
    private static partial void LogRevokeFailed(ILogger logger, Guid agentId, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Agent key rotation failed from the console for agent {AgentId}.")]
    private static partial void LogRotateFailed(ILogger logger, Guid agentId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Agent action audit logging failed from the console (the action itself already committed).")]
    private static partial void LogAuditFailed(ILogger logger, Exception exception);

    /// <summary>
    /// Journalise l'action opérateur (parité d'audit avec les endpoints API05) en BEST-EFFORT : la commande
    /// PIV05 a déjà commité (clé émise / agent révoqué — irréversible). Un échec d'audit (INV-AUDIT-002 : le
    /// logger ne devrait jamais lever) est tracé puis avalé — il ne doit JAMAIS faire perdre la clé unique ni
    /// transformer un succès irréversible en échec.
    /// </summary>
    private async Task AuditBestEffortAsync(
        string entityId,
        string activityType,
        string description,
        object? metadata,
        CancellationToken cancellationToken)
    {
        try
        {
            var actor = _actorContext.Current;
            await _activityLogger.LogActivityAsync(
                AgentEntityType,
                entityId,
                activityType,
                description,
                ActorId(actor),
                metadata: metadata,
                companyId: actor.CompanyId,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogAuditFailed(_logger, ex);
        }
    }

    private async Task<int> ResolveSilentHoursAsync(CancellationToken cancellationToken)
    {
        var companyId = await _tenantSettings.GetCurrentCompanyId(cancellationToken).ConfigureAwait(false);
        if (companyId is not { } id)
        {
            return DefaultAgentSilentHours;
        }

        var thresholds = await _tenantSettings.GetAlertThresholds(id, cancellationToken).ConfigureAwait(false);
        return thresholds?.AgentSilentHours ?? DefaultAgentSilentHours;
    }
}
