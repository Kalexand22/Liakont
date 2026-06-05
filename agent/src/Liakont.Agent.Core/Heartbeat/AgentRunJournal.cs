namespace Liakont.Agent.Core.Heartbeat;

using System;
using System.Collections.Generic;
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
/// actuelle (effacée par un run sain). La lecture est TOLÉRANTE : une valeur horaire corrompue
/// (héritée, écriture partielle) est traitée comme absente (<c>null</c>), jamais une exception — le
/// heartbeat doit rester silencieux et ne jamais tuer son thread (F12 §2.5).
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

    private static readonly string[] AllKeys =
    {
        LastRunStartedKey,
        LastRunCompletedKey,
        LastRunOutcomeKey,
        LastErrorKey,
        LastSuccessfulSyncKey,
    };

    private readonly LocalQueue _queue;

    /// <summary>Crée un journal de run au-dessus de la file locale.</summary>
    /// <param name="queue">File locale (porteuse de la table <c>agent_state</c>).</param>
    public AgentRunJournal(LocalQueue queue)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
    }

    /// <summary>Horodatage de début du dernier run (UTC), ou <c>null</c> si aucun run/valeur illisible.</summary>
    public DateTime? LastRunStartedUtc => ParseUtc(_queue.GetState(LastRunStartedKey));

    /// <summary>Horodatage de fin du dernier run (UTC), ou <c>null</c> si aucun run/valeur illisible.</summary>
    public DateTime? LastRunCompletedUtc => ParseUtc(_queue.GetState(LastRunCompletedKey));

    /// <summary>Résultat du dernier run, ou <c>null</c> si aucun run.</summary>
    public string? LastRunOutcome => _queue.GetState(LastRunOutcomeKey);

    /// <summary>Dernière erreur locale, ou <c>null</c> (aucune erreur courante).</summary>
    public string? LastError => _queue.GetState(LastErrorKey);

    /// <summary>Dernière synchronisation réussie (UTC), ou <c>null</c> si aucune/valeur illisible.</summary>
    public DateTime? LastSuccessfulSyncUtc => ParseUtc(_queue.GetState(LastSuccessfulSyncKey));

    /// <summary>
    /// Lit toute l'issue du dernier run en UN SEUL verrou (lecture non « déchirée »). Combinée à
    /// l'écriture ATOMIQUE de la fin de run (<see cref="RecordRunFinished"/> via
    /// <see cref="LocalQueue.SetStates"/>), elle garantit que le trio fin/résultat/erreur provient
    /// toujours du même run. NB : pendant un run EN COURS, <see cref="AgentRunJournalSnapshot.LastRunStartedUtc"/>
    /// peut être postérieur à <see cref="AgentRunJournalSnapshot.LastRunCompletedUtc"/> (le nouveau run
    /// a démarré, l'ancien est la dernière fin connue) — c'est l'état RÉEL, pas une incohérence.
    /// </summary>
    /// <returns>Une photographie non déchirée de l'issue du dernier run.</returns>
    public AgentRunJournalSnapshot ReadSnapshot()
    {
        IReadOnlyDictionary<string, string?> states = _queue.GetStates(AllKeys);
        return new AgentRunJournalSnapshot(
            lastRunStartedUtc: ParseUtc(states[LastRunStartedKey]),
            lastRunCompletedUtc: ParseUtc(states[LastRunCompletedKey]),
            lastRunOutcome: states[LastRunOutcomeKey],
            lastError: states[LastErrorKey],
            lastSuccessfulSyncUtc: ParseUtc(states[LastSuccessfulSyncKey]));
    }

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

        // Trio écrit ATOMIQUEMENT (une transaction, un verrou) : un heartbeat concurrent ne lit jamais
        // une fin de run « déchirée » (p. ex. completed = run N+1 avec outcome = run N).
        _queue.SetStates(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [LastRunCompletedKey] = FormatUtc(completedUtc),
            [LastRunOutcomeKey] = outcome,
            [LastErrorKey] = error,
        });
    }

    /// <summary>Enregistre une synchronisation réussie (au moins un push abouti) avec la plateforme.</summary>
    public void RecordSuccessfulSync(DateTime syncUtc) =>
        _queue.SetState(LastSuccessfulSyncKey, FormatUtc(syncUtc));

    private static string FormatUtc(DateTime value) =>
        value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

    private static DateTime? ParseUtc(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        try
        {
            return DateTime.Parse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
        }
        catch (FormatException)
        {
            // Valeur héritée/partielle illisible : traitée comme absente, jamais propagée (F12 §2.5).
            return null;
        }
    }
}
