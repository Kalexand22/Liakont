namespace Liakont.Agent.Cli.Diagnostics;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Liakont.Agent.Core.Storage;

/// <summary>
/// Photographie en LECTURE SEULE de la file locale de push (F12 §2.3, commande <c>show-queue</c>).
/// La file est un tampon technique, jamais une piste d'audit ; le CLI ne fait que l'inspecter.
/// </summary>
internal sealed class QueueSnapshot
{
    public QueueSnapshot(int total, int pending, int inProgress, int error, IReadOnlyList<QueuedItem> items)
    {
        Total = total;
        Pending = pending;
        InProgress = inProgress;
        Error = error;
        Items = new ReadOnlyCollection<QueuedItem>(new List<QueuedItem>(items ?? Array.Empty<QueuedItem>()));
    }

    /// <summary>Nombre total d'éléments dans la file (tous statuts).</summary>
    public int Total { get; }

    /// <summary>Éléments en attente de push.</summary>
    public int Pending { get; }

    /// <summary>Éléments en cours (push émis, acquittement terminal attendu — ADR-0012).</summary>
    public int InProgress { get; }

    /// <summary>Éléments en erreur (échec non terminal signalé au heartbeat).</summary>
    public int Error { get; }

    /// <summary>Éléments à traiter (en attente puis en erreur) listés pour l'opérateur.</summary>
    public IReadOnlyList<QueuedItem> Items { get; }
}
