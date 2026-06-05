namespace Liakont.Agent.Cli.Commands;

using System;
using System.Collections.Generic;
using System.IO;
using Liakont.Agent.Cli.Diagnostics;
using Liakont.Agent.Core.Storage;

/// <summary>
/// Commande <c>show-queue</c> (F12 §2.1, §2.3) : affiche l'état de la file locale de push en LECTURE
/// SEULE — total, répartition par statut, et les éléments à traiter (en attente puis en erreur). La
/// lecture est injectée (testable avec une doublure).
/// </summary>
internal sealed class ShowQueueCommand : ICliCommand
{
    private readonly Func<QueueSnapshot> _readSnapshot;

    public ShowQueueCommand(Func<QueueSnapshot> readSnapshot)
    {
        _readSnapshot = readSnapshot ?? throw new ArgumentNullException(nameof(readSnapshot));
    }

    public string Name => "show-queue";

    public string Description => "Affiche l'état de la file locale de push (en attente, en cours, en erreur).";

    public int Execute(IReadOnlyList<string> args, TextWriter output)
    {
        QueueSnapshot snapshot;
        try
        {
            snapshot = _readSnapshot();
        }
        catch (Exception ex)
        {
            output.WriteLine(CliFormat.Fail("Lecture de la file locale impossible : " + ex.Message));
            return CliExitCode.ExecutionError;
        }

        if (snapshot.Total == 0)
        {
            output.WriteLine("File locale vide — aucun élément en attente de push.");
            return CliExitCode.Ok;
        }

        output.WriteLine($"File locale : {snapshot.Total} élément(s) — {snapshot.Pending} en attente, {snapshot.InProgress} en cours, {snapshot.Error} en erreur.");
        foreach (QueuedItem item in snapshot.Items)
        {
            string line = $"  #{item.Id} {item.Kind} « {item.SourceReference} » [{item.Status}] tentatives={item.Attempts}";
            if (!string.IsNullOrEmpty(item.LastError))
            {
                line += $" — {item.LastError}";
            }

            output.WriteLine(line);
        }

        int listed = snapshot.Items.Count;
        int actionable = snapshot.Pending + snapshot.Error;
        if (actionable > listed)
        {
            output.WriteLine($"  … ({actionable - listed} élément(s) supplémentaire(s) non affiché(s)).");
        }

        return CliExitCode.Ok;
    }
}
