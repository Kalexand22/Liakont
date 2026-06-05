namespace Liakont.Agent.Cli.Diagnostics;

using System.Collections.Generic;
using System.Linq;
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
            int total = queue.Count();

            // PeekPending ne renvoie que les éléments à pousser (En attente + En erreur) ; on les compte
            // tous pour des chiffres exacts, puis on déduit « en cours » du total (3 statuts possibles).
            IReadOnlyList<QueuedItem> actionable = queue.PeekPending(int.MaxValue);
            int pending = actionable.Count(i => i.Status == QueueItemStatus.Pending);
            int error = actionable.Count(i => i.Status == QueueItemStatus.Error);

            int inProgress = total - pending - error;
            if (inProgress < 0)
            {
                inProgress = 0;
            }

            IReadOnlyList<QueuedItem> listed = actionable.Take(MaxItemsListed).ToList();
            return new QueueSnapshot(total, pending, inProgress, error, listed);
        }
    }
}
