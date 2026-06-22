namespace Liakont.PaClients.ChorusPro;

/// <summary>
/// Politique de retry des lectures TRANSITOIRES du plug-in Chorus Pro (relecture <c>consulterCR</c>, F18 §4).
/// Seul le transitoire (5xx / réseau / timeout) est ré-essayé : la relecture d'état est NATURELLEMENT
/// idempotente (aucune écriture, aucun dépôt), donc sans risque de double facture (CLAUDE.md n°3, cohérent
/// D8). Les rejets métier (4xx) et l'auth (401/403, déjà retentée une fois par le client) ne sont JAMAIS
/// ré-essayés. Modèle : <c>SuperPdpRetryPolicy</c> (F14 §4.1).
/// </summary>
internal sealed record ChorusProRetryPolicy
{
    /// <summary>
    /// Délais d'attente entre tentatives, dans l'ordre : <c>Backoffs[i]</c> précède la tentative <c>i+1</c>.
    /// Le nombre total de tentatives est <c>Backoffs.Count + 1</c>. Jamais <c>null</c>.
    /// </summary>
    public required IReadOnlyList<TimeSpan> Backoffs { get; init; }

    /// <summary>Nombre de RE-tentatives après la première (= <see cref="Backoffs"/>.Count).</summary>
    public int RetryCount => Backoffs.Count;

    /// <summary>Politique par défaut (production) : 3 re-tentatives à délais croissants (5 s, 30 s, 2 min).</summary>
    public static ChorusProRetryPolicy Default { get; } = new()
    {
        Backoffs = [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2)],
    };

    /// <summary>Politique sans délai (tests) : même nombre de re-tentatives, délais nuls.</summary>
    /// <param name="retries">Nombre de re-tentatives (par défaut 3).</param>
    public static ChorusProRetryPolicy NoDelay(int retries = 3)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(retries);
        return new ChorusProRetryPolicy { Backoffs = Enumerable.Repeat(TimeSpan.Zero, retries).ToArray() };
    }
}
