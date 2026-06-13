namespace Liakont.PaClients.SuperPdp;

/// <summary>
/// Politique de retry des erreurs TRANSITOIRES (réseau / 5xx / timeout — même grille que B2Brouter
/// F05 §4.1) : une relecture d'état qui échoue de façon re-tentable est réessayée selon le calendrier
/// de backoff, puis dégradée en
/// <see cref="Modules.Transmission.Contracts.PaSendState.TechnicalError"/> (re-tentable au prochain run).
/// <para>
/// Le calendrier est <b>injectable</b> uniquement pour que les tests unitaires s'exécutent sans attente
/// réelle (backoff zéro). Le nombre de réessais est la longueur du calendrier ; aucun retry n'est
/// appliqué aux rejets métier (4xx, 200 + <c>errors[]</c>). La cadence exacte de Super PDP n'étant pas
/// documentée, la valeur de PRODUCTION reprend l'ordre de grandeur éprouvé (5 s / 30 s / 2 min) — à
/// confirmer/ajuster en sandbox (PAS03) ; ce n'est pas une règle fiscale (CLAUDE.md n°2).
/// </para>
/// </summary>
internal sealed record SuperPdpRetryPolicy
{
    /// <summary>
    /// Délais d'attente AVANT chaque réessai, dans l'ordre. La longueur fixe le nombre de réessais :
    /// le nombre total de tentatives est <c>Backoffs.Count + 1</c>. Jamais <c>null</c>.
    /// </summary>
    public required IReadOnlyList<TimeSpan> Backoffs { get; init; }

    /// <summary>Nombre de réessais après la tentative initiale (= nombre de délais de backoff).</summary>
    public int RetryCount => Backoffs.Count;

    /// <summary>Politique de PRODUCTION : 3 réessais, backoff 5 s → 30 s → 2 min (ordre de grandeur éprouvé).</summary>
    public static SuperPdpRetryPolicy Default { get; } = new()
    {
        Backoffs = [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2)],
    };

    /// <summary>
    /// Politique de TEST : <paramref name="retries"/> réessais sans aucune attente (backoff zéro), pour
    /// exercer la boucle de retry sans bloquer la suite unitaire.
    /// </summary>
    /// <param name="retries">Nombre de réessais souhaités (≥ 0).</param>
    public static SuperPdpRetryPolicy NoDelay(int retries = 3)
    {
        if (retries < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(retries), retries, "Le nombre de réessais ne peut être négatif.");
        }

        return new SuperPdpRetryPolicy { Backoffs = Enumerable.Repeat(TimeSpan.Zero, retries).ToArray() };
    }
}
