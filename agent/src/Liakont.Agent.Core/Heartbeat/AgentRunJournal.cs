namespace Liakont.Agent.Core.Heartbeat;

using System;
using System.Globalization;
using Liakont.Agent.Core.Storage;

/// <summary>
/// Mémoire technique de l'issue du dernier run et de la dernière synchronisation réussie, persistée
/// dans <c>agent_state</c> (table de la file locale, F12 §2.3). Le run d'extraction (AGT02) y inscrit
/// son issue ; le heartbeat (AGT03) la relit pour la remonter à la plateforme (F12 §2.5 « horodatage
/// du dernier run, résultat du dernier run, dernières erreurs » ; §5.2 alerte « Run manqué »).
/// <para>
/// Ce n'est PAS une piste d'audit : la piste d'audit légale vit sur la plateforme. C'est un état
/// COURANT (une seule valeur par clé, écrasée à chaque run) — la dernière erreur reflète la santé
/// actuelle (effacée par un run sain). <see cref="LocalQueue.GetState"/> / <see cref="LocalQueue.SetState"/>
/// sont déjà sérialisés par le verrou interne de la file (sûreté multi-thread service/heartbeat).
/// </para>
/// </summary>
public sealed class AgentRunJournal
{
    /// <summary>Clé d'<c>agent_state</c> : horodatage de début du dernier run (UTC).</summary>
    public const string LastRunStartedKey = "run.last_started.utc";

    /// <summary>Clé d'<c>agent_state</c> : horodatage de fin du dernier run (UTC).</summary>
    public const string LastRunCompletedKey = "run.last_completed.utc";

    /// <summary>Clé d'<c>agent_state</c> : résultat du dernier run (libellé technique).</summary>
    public const string LastRunOutcomeKey = "run.last_outcome";

    /// <summary>Clé d'<c>agent_state</c> : dernière erreur locale lisible (effacée par un run sain).</summary>
    public const string LastErrorKey = "run.last_error";

    /// <summary>Clé d'<c>agent_state</c> : dernière synchronisation (push réussi) avec la plateforme (UTC).</summary>
    public const string LastSuccessfulSyncKey = "sync.last_successful.utc";

    private readonly LocalQueue _queue;

    /// <summary>Crée un journal de run au-dessus de la file locale.</summary>
    /// <param name="queue">File locale (porteuse de la table <c>agent_state</c>).</param>
    public AgentRunJournal(LocalQueue queue)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
    }

    /// <summary>Horodatage de début du dernier run (UTC), ou <c>null</c> si aucun run.</summary>
    public DateTime? LastRunStartedUtc => ReadUtc(LastRunStartedKey);

    /// <summary>Horodatage de fin du dernier run (UTC), ou <c>null</c> si aucun run.</summary>
    public DateTime? LastRunCompletedUtc => ReadUtc(LastRunCompletedKey);

    /// <summary>Résultat du dernier run, ou <c>null</c> si aucun run.</summary>
    public string? LastRunOutcome => _queue.GetState(LastRunOutcomeKey);

    /// <summary>Dernière erreur locale, ou <c>null</c> (aucune erreur courante).</summary>
    public string? LastError => _queue.GetState(LastErrorKey);

    /// <summary>Dernière synchronisation réussie (UTC), ou <c>null</c> si aucune.</summary>
    public DateTime? LastSuccessfulSyncUtc => ReadUtc(LastSuccessfulSyncKey);

    /// <summary>Enregistre le début d'un run.</summary>
    public void RecordRunStarted(DateTime startedUtc) =>
        _queue.SetState(LastRunStartedKey, FormatUtc(startedUtc));

    /// <summary>
    /// Enregistre la fin d'un run : horodatage, résultat, et la dernière erreur courante
    /// (<paramref name="error"/> = <c>null</c> efface l'erreur — un run sain remet la santé à « OK »).
    /// </summary>
    /// <param name="completedUtc">Horodatage de fin (UTC).</param>
    /// <param name="outcome">Libellé du résultat (ex. « Success », « SourceUnavailable »).</param>
    /// <param name="error">Dernière erreur lisible, ou <c>null</c> pour un run sans erreur.</param>
    public void RecordRunFinished(DateTime completedUtc, string outcome, string? error = null)
    {
        if (string.IsNullOrWhiteSpace(outcome))
        {
            throw new ArgumentException("Le résultat du run est requis.", nameof(outcome));
        }

        _queue.SetState(LastRunCompletedKey, FormatUtc(completedUtc));
        _queue.SetState(LastRunOutcomeKey, outcome);
        _queue.SetState(LastErrorKey, error);
    }

    /// <summary>Enregistre une synchronisation réussie (au moins un push abouti) avec la plateforme.</summary>
    public void RecordSuccessfulSync(DateTime syncUtc) =>
        _queue.SetState(LastSuccessfulSyncKey, FormatUtc(syncUtc));

    private static string FormatUtc(DateTime value) =>
        value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

    private DateTime? ReadUtc(string key)
    {
        string? raw = _queue.GetState(key);
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        return DateTime.Parse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
    }
}
