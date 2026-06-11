namespace Liakont.Host.Documents;

using System;

/// <summary>
/// Politique d'attente (bornée) du résultat d'un run d'envoi déclenché manuellement (FIX05). Le job SEND est
/// asynchrone (publié sur la queue SYSTÈME, exécuté par le <c>JobWorker</c> null-tenant — ADR-0016) : pour
/// remonter à l'opérateur le RÉSULTAT de fin (émis / partiel / rien + motif) sans polling manuel, la console
/// sonde brièvement le journal des exécutions (<c>pipeline.run_logs</c>) jusqu'à ce que le run apparaisse
/// clôturé. Au-delà du budget, on dégrade gracieusement (« le résultat apparaîtra dans le journal »). Les
/// valeurs sont injectables pour que les tests sondent à intervalle nul (déterministes, sans attente réelle).
/// </summary>
/// <param name="PollInterval">Délai entre deux sondes du journal (0 en test).</param>
/// <param name="MaxAttempts">Nombre maximum de sondes avant dégradation gracieuse.</param>
internal sealed record SendRunWaitPolicy(TimeSpan PollInterval, int MaxAttempts)
{
    /// <summary>Budget de production : ~15 s (30 sondes × 500 ms) — couvre la prise en charge du job par le worker.</summary>
    public static SendRunWaitPolicy Default { get; } = new(TimeSpan.FromMilliseconds(500), 30);
}
