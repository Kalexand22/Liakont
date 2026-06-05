namespace Liakont.Agent.Core.Hosting;

using System;
using System.Threading;
using Liakont.Agent.Core;
using Liakont.Agent.Core.Extraction;
using Liakont.Agent.Core.Heartbeat;
using Liakont.Agent.Core.Logging;
using Liakont.Agent.Core.Storage;
using Liakont.Agent.Core.Time;
using Liakont.Agent.Core.Transport;

/// <summary>
/// Cycle complet de l'agent (F12 §2.2) : un run d'EXTRACTION (sur la fenêtre [filigrane, maintenant[)
/// suivi d'un DRAINAGE de la file vers la plateforme. Conçu pour être branché tel quel comme cycle de
/// l'<see cref="AgentBackgroundRunner"/> (sa signature <c>Run(CancellationToken)</c> correspond au
/// délégué attendu). Un échec d'extraction (source momentanément indisponible ou schéma incompatible)
/// n'EMPÊCHE PAS le drainage : les éléments déjà en file (runs précédents, reprise après coupure) sont
/// poussés quand même.
/// <para>
/// La fenêtre d'extraction part du filigrane : tant qu'aucun filigrane n'existe et qu'aucune borne
/// n'est imposée par la plateforme (AGT03, <c>ExtractFromUtc</c>), la fenêtre est vide — l'agent
/// n'invente aucune profondeur de rattrapage (CLAUDE.md n°2). Le pilotage de la planification (heures,
/// catchUpOnStart, surcharge plateforme) est porté par l'hôte et AGT03.
/// </para>
/// </summary>
public sealed class AgentRunCycle
{
    private readonly IExtractor _extractor;
    private readonly ExtractionCycle _extractionCycle;
    private readonly QueueDrainer _drainer;
    private readonly LocalQueue _queue;
    private readonly IClock _clock;
    private readonly IAgentLog _log;
    private readonly AgentRunJournal? _journal;

    /// <summary>Crée un cycle d'agent.</summary>
    /// <param name="extractor">Extracteur source configuré.</param>
    /// <param name="extractionCycle">Cycle d'extraction (EXTRACT → COLLECT → enqueue).</param>
    /// <param name="drainer">Drainage de la file vers la plateforme.</param>
    /// <param name="queue">File locale (lecture du filigrane d'extraction).</param>
    /// <param name="clock">Horloge.</param>
    /// <param name="log">Journal de l'agent.</param>
    /// <param name="journal">
    /// Journal du dernier run / dernière sync (AGT03), pour que le heartbeat remonte l'issue du run
    /// (F12 §2.5). Optionnel : un cycle sans journal ne mémorise rien (rétrocompatible AGT02).
    /// </param>
    public AgentRunCycle(
        IExtractor extractor,
        ExtractionCycle extractionCycle,
        QueueDrainer drainer,
        LocalQueue queue,
        IClock clock,
        IAgentLog log,
        AgentRunJournal? journal = null)
    {
        _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
        _extractionCycle = extractionCycle ?? throw new ArgumentNullException(nameof(extractionCycle));
        _drainer = drainer ?? throw new ArgumentNullException(nameof(drainer));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _journal = journal;
    }

    /// <summary>Exécute un cycle complet : extraction (best-effort) puis drainage.</summary>
    /// <param name="cancellationToken">Jeton d'arrêt propre (la barrière d'hôte attend la fin du cycle).</param>
    public void Run(CancellationToken cancellationToken)
    {
        DateTime now = _clock.UtcNow;
        _journal?.RecordRunStarted(now);

        DateTime? watermark = _queue.GetExtractionWatermarkUtc();

        // La fenêtre part du filigrane ; l'adaptateur DOIT extraire sur un axe « disponible depuis »
        // (cf. IExtractor.ExtractDocuments) pour qu'un document anté-daté saisi après l'avancée du
        // filigrane reste extractible. L'anti-re-push par (source_reference, payload_hash) rend toute
        // ré-extraction (et toute fenêtre de recouvrement décidée par AGT03) idempotente.
        DateTime from = watermark ?? now;
        if (from > now)
        {
            from = now; // horloge reculée : jamais de fenêtre négative.
        }

        // Issue du run remontée au heartbeat (F12 §2.5 « résultat du dernier run, dernières erreurs »).
        string outcome = "Success";
        string? error = null;
        try
        {
            _extractionCycle.Run(_extractor, from, now);
        }
        catch (SourceUnavailableException ex)
        {
            // Réessayable (R7) : le drainage continue, le run sera repris au cycle suivant.
            outcome = "SourceUnavailable";
            error = ex.Message;
            _log.Warn($"Extraction reportée — source momentanément indisponible : {ex.Message}");
        }
        catch (SourceSchemaException ex)
        {
            // Fatal (R7) : intervention requise. Le drainage des éléments déjà en file continue.
            outcome = "SourceSchema";
            error = ex.Message;
            _log.Error("Extraction impossible — schéma de source incompatible (intervention requise).", ex);
        }

        DrainResult drain = _drainer.DrainOnce(cancellationToken);

        if (_journal != null)
        {
            // Sync réussie = au moins un push abouti (accepté/acquitté côté plateforme — F12 §2.5).
            if (drain.DocumentsInProgress + drain.DocumentsAcknowledged + drain.PdfsAcknowledged > 0)
            {
                _journal.RecordSuccessfulSync(_clock.UtcNow);
            }

            // Extraction OK mais drainage stoppé par un échec (clé invalide, surcharge, réseau) :
            // le refléter dans l'issue, sans écraser un échec d'extraction déjà plus grave.
            if (outcome == "Success" && drain.StoppedBy is PlatformResponseKind stopped && stopped != PlatformResponseKind.Ok)
            {
                outcome = "DrainIncomplete:" + stopped;
                error = $"Drainage interrompu ({stopped}).";
            }

            _journal.RecordRunFinished(_clock.UtcNow, outcome, error);
        }
    }
}
