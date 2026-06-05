namespace Liakont.Agent.Core.Heartbeat;

using System;
using System.Collections.Generic;
using Liakont.Agent.Contracts;
using Liakont.Agent.Contracts.Transport;
using Liakont.Agent.Core.Configuration;
using Liakont.Agent.Core.Logging;
using Liakont.Agent.Core.Storage;
using Liakont.Agent.Core.Time;
using Liakont.Agent.Core.Transport;

/// <summary>
/// Remontée d'état et pilotage centralisé de l'agent (F12 §2.5, §3.2 — AGT03). Émet un heartbeat
/// périodique COMPLET (version, état du service, file, dernier run, erreurs, disque), applique la
/// configuration effective renvoyée par la plateforme (planification qui surcharge le fichier local,
/// période imposée, version attendue), et charge la configuration au démarrage du service.
/// <para>
/// PRINCIPE F12 §2.5 : l'échec d'un heartbeat est SILENCIEUX côté agent (WARN local, réessai au cycle
/// suivant). C'est la PLATEFORME qui détecte l'absence de heartbeat (dead-man's switch, F12 §5.1) —
/// jamais l'agent qui s'auto-alerte. Aucune méthode ici ne lève sur un échec réseau : le transport
/// renvoie une catégorie typée, jamais une exception.
/// </para>
/// <para>
/// AUCUNE logique métier (CLAUDE.md n°6) : l'agent rapporte des compteurs et applique une
/// configuration opaque ; il n'interprète aucun état fiscal.
/// </para>
/// </summary>
public sealed class HeartbeatReporter
{
    private readonly IPlatformClient _client;
    private readonly LocalQueue _queue;
    private readonly AgentRunJournal _journal;
    private readonly IDiskFreeSpaceProbe _diskProbe;
    private readonly PlatformConfigurationStore _configStore;
    private readonly IClock _clock;
    private readonly IAgentLog _log;
    private readonly string _agentVersion;
    private readonly string _contractVersion;
    private readonly string _serviceState;

    /// <summary>Crée un rapporteur de heartbeat.</summary>
    /// <param name="client">Couture de transport vers la plateforme.</param>
    /// <param name="queue">File locale (taille, erreurs, état persistant).</param>
    /// <param name="journal">Journal du dernier run / dernière sync.</param>
    /// <param name="diskProbe">Sonde d'espace disque (best-effort).</param>
    /// <param name="configStore">Store de la dernière configuration plateforme reçue.</param>
    /// <param name="clock">Horloge.</param>
    /// <param name="log">Journal de l'agent (messages opérateur en français).</param>
    /// <param name="agentVersion">Version de l'agent installé (remontée à la plateforme).</param>
    /// <param name="serviceState">État du service à remonter (défaut « Running »).</param>
    /// <param name="contractVersion">Version de contrat émise (défaut : celle de l'assembly).</param>
    public HeartbeatReporter(
        IPlatformClient client,
        LocalQueue queue,
        AgentRunJournal journal,
        IDiskFreeSpaceProbe diskProbe,
        PlatformConfigurationStore configStore,
        IClock clock,
        IAgentLog log,
        string agentVersion,
        string serviceState = "Running",
        string? contractVersion = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _diskProbe = diskProbe ?? throw new ArgumentNullException(nameof(diskProbe));
        _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        if (string.IsNullOrWhiteSpace(agentVersion))
        {
            throw new ArgumentException("La version de l'agent est requise.", nameof(agentVersion));
        }

        _agentVersion = agentVersion;
        _serviceState = string.IsNullOrWhiteSpace(serviceState) ? "Running" : serviceState;
        _contractVersion = string.IsNullOrWhiteSpace(contractVersion) ? AgentContractVersion.ContractVersion : contractVersion!;
    }

    /// <summary>
    /// Assemble la photographie d'état courante (F12 §2.5) à partir de la file locale, du journal de
    /// run et de la sonde disque. N'émet rien sur le réseau.
    /// </summary>
    /// <returns>La photographie d'état.</returns>
    public AgentHealthSnapshot GatherSnapshot()
    {
        IReadOnlyDictionary<QueueItemStatus, int> byStatus = _queue.CountByStatus();
        int total = 0;
        foreach (int count in byStatus.Values)
        {
            total += count;
        }

        byStatus.TryGetValue(QueueItemStatus.Error, out int errorCount);

        return new AgentHealthSnapshot(
            serviceState: _serviceState,
            pushQueueDepth: total,
            pushQueueErrorCount: errorCount,
            lastRunStartedUtc: _journal.LastRunStartedUtc,
            lastRunCompletedUtc: _journal.LastRunCompletedUtc,
            lastRunOutcome: _journal.LastRunOutcome,
            lastError: _journal.LastError,
            lastSuccessfulSyncUtc: _journal.LastSuccessfulSyncUtc,
            diskFreeBytes: _diskProbe.GetAvailableFreeBytes());
    }

    /// <summary>
    /// Émet un heartbeat et applique la configuration effective renvoyée. Échec = WARN local et repli
    /// sur la configuration locale (F12 §2.5) — ne lève jamais.
    /// </summary>
    /// <returns>Le résultat du heartbeat (catégorie + configuration appliquée si 200).</returns>
    public HeartbeatOutcome SendHeartbeat()
    {
        AgentHealthSnapshot snapshot = GatherSnapshot();
        var request = new HeartbeatRequestDto(
            contractVersion: _contractVersion,
            agentVersion: _agentVersion,
            sentAtUtc: _clock.UtcNow,
            lastSuccessfulSyncUtc: snapshot.LastSuccessfulSyncUtc,
            serviceState: snapshot.ServiceState,
            pushQueueDepth: snapshot.PushQueueDepth,
            pushQueueErrorCount: snapshot.PushQueueErrorCount,
            lastRunStartedUtc: snapshot.LastRunStartedUtc,
            lastRunCompletedUtc: snapshot.LastRunCompletedUtc,
            lastRunOutcome: snapshot.LastRunOutcome,
            lastError: snapshot.LastError,
            diskFreeBytes: snapshot.DiskFreeBytes);

        HeartbeatOutcome outcome = _client.SendHeartbeat(request);
        ApplyConfiguration(outcome.Kind, outcome.Configuration, outcome.Reason, "heartbeat");
        return outcome;
    }

    /// <summary>
    /// Charge la configuration au DÉMARRAGE du service (GET /configuration). Si la plateforme est
    /// joignable, l'agent démarre avec sa configuration ; sinon il démarre avec sa configuration
    /// locale (F12 §2.5) — ne lève jamais.
    /// </summary>
    /// <returns>Le résultat de la lecture (catégorie + configuration appliquée si 200).</returns>
    public ConfigurationOutcome LoadStartupConfiguration()
    {
        ConfigurationOutcome outcome = _client.GetConfiguration();
        ApplyConfiguration(outcome.Kind, outcome.Configuration, outcome.Reason, "démarrage");
        return outcome;
    }

    /// <summary>
    /// Résout le plan d'extraction effectif à partir du fichier local et de la dernière configuration
    /// plateforme connue : la planification plateforme surcharge le fichier local quand elle est
    /// présente (F12 §6.1). Sans configuration plateforme mémorisée, le plan est 100 % local.
    /// </summary>
    /// <param name="local">La configuration d'extraction du fichier local.</param>
    /// <returns>Le plan d'extraction effectif.</returns>
    public EffectiveExtractionPlan ResolveEffectivePlan(ExtractionConfig local) =>
        EffectiveExtractionPlan.Resolve(local, _configStore.TryGet());

    private static string FormatReason(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? string.Empty : $" — {reason}";

    private void ApplyConfiguration(PlatformResponseKind kind, AgentConfigurationDto? configuration, string? reason, string context)
    {
        if (kind != PlatformResponseKind.Ok)
        {
            // Échec silencieux (F12 §2.5) : WARN local, l'agent garde sa configuration et réessaiera.
            _log.Warn($"Plateforme injoignable au {context} (catégorie {kind}{FormatReason(reason)}). L'agent poursuit avec sa configuration locale.");
            return;
        }

        if (configuration is null)
        {
            // 200 sans configuration : la plateforme a accusé réception sans pousser de réglage.
            _log.Info($"Plateforme jointe au {context} : aucune configuration à appliquer (réglages locaux conservés).");
            return;
        }

        _configStore.Save(configuration);
        _log.Info($"Configuration plateforme reçue au {context} et mémorisée (planification {(string.IsNullOrWhiteSpace(configuration.ExtractionSchedule) ? "locale" : "plateforme")}).");
    }
}
