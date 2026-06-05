namespace Liakont.Agent.Cli.Diagnostics;

using System.Collections.Generic;
using Liakont.Agent.Core.Storage;
using Liakont.Agent.Core.Time;

/// <summary>
/// Lit une <see cref="QueueSnapshot"/> de la file locale (F12 §2.3, commande <c>show-queue</c>) en
/// LECTURE SEULE : total, répartition par statut, et la liste des éléments à traiter (en attente puis
/// en erreur). Aucune mutation — la file est un tampon technique, pas une piste d'audit.
/// </summary>
internal static class LocalQueueSnapshotReader
{
    // Borne d'AFFICHAGE des éléments en attente/erreur : un diagnostic n'a pas vocation à dérouler
    // des milliers de lignes. Les COMPTAGES restent exacts (calculés sur l'ensemble, pas sur l'extrait).
    private const int MaxItemsListed = 200;

    public static QueueSnapshot Read(string databasePath)
    {
        using (var queue = new LocalQueue(databasePath, new SystemClock()))
        {
            IReadOnlyDictionary<QueueItemStatus, int> counts = queue.CountByStatus();
            int pending = GetCount(counts, QueueItemStatus.Pending);
            int inProgress = GetCount(counts, QueueItemStatus.InProgress);
            int error = GetCount(counts, QueueItemStatus.Error);
            int total = pending + inProgress + error;

            IReadOnlyList<QueuedItem> listed = queue.PeekPending(MaxItemsListed);
            return new QueueSnapshot(total, pending, inProgress, error, listed);
        }
    }

    private static int GetCount(IReadOnlyDictionary<QueueItemStatus, int> counts, QueueItemStatus status)
    {
        return counts.TryGetValue(status, out int value) ? value : 0;
    }
}
