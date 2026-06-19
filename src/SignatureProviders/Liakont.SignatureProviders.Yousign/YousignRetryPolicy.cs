namespace Liakont.SignatureProviders.Yousign;

/// <summary>
/// Politique de retry des appels sortants Yousign (ADR-0029 §4 ; INV-YOUSIGN-5). Backoff EXPONENTIEL +
/// JITTER sur les réponses <c>429 Too Many Requests</c> (et le transitoire réseau / 5xx). Les rejets métier
/// (4xx hors 429) et l'authentification (401/403) ne sont JAMAIS ré-essayés. Le calendrier est injectable
/// UNIQUEMENT pour que les tests s'exécutent sans attente réelle (<see cref="NoDelay"/>) ; la valeur de
/// PRODUCTION (<see cref="Default"/>) est figée.
/// </summary>
internal sealed record YousignRetryPolicy
{
    /// <summary>
    /// Politique de PRODUCTION : 3 réessais, backoff exponentiel 1 s → 5 s → 30 s, jitter ±20 %
    /// (ADR-0029 §4 — borne raisonnable, jamais une cadence fiscale inventée).
    /// </summary>
    public static YousignRetryPolicy Default { get; } = new()
    {
        Backoffs = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)],
        JitterFraction = 0.2,
    };

    /// <summary>Délais de base AVANT chaque réessai, dans l'ordre. La longueur fixe le nombre de réessais.</summary>
    public required IReadOnlyList<TimeSpan> Backoffs { get; init; }

    /// <summary>Fraction MAX de jitter ajoutée au délai de base (0.0 = aucun jitter, 0.2 = ±20 %).</summary>
    public double JitterFraction { get; init; }

    /// <summary>Nombre de réessais après la tentative initiale.</summary>
    public int RetryCount => Backoffs.Count;

    /// <summary>Politique de TEST : <paramref name="retries"/> réessais sans aucune attente (backoff zéro).</summary>
    /// <param name="retries">Nombre de réessais (≥ 0).</param>
    public static YousignRetryPolicy NoDelay(int retries = 3)
    {
        if (retries < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(retries), retries, "Le nombre de réessais ne peut être négatif.");
        }

        return new YousignRetryPolicy
        {
            Backoffs = Enumerable.Repeat(TimeSpan.Zero, retries).ToArray(),
            JitterFraction = 0,
        };
    }

    /// <summary>
    /// Délai effectif avant le réessai <paramref name="attempt"/> (base + jitter borné). Le jitter casse la
    /// synchronisation des clients sous 429 (thundering herd) ; <paramref name="jitterSample"/> ∈ [0,1).
    /// </summary>
    /// <param name="attempt">Index du réessai (0 = premier réessai).</param>
    /// <param name="jitterSample">Échantillon aléatoire uniforme dans [0,1) (injecté pour le déterminisme).</param>
    public TimeSpan DelayFor(int attempt, double jitterSample)
    {
        var baseDelay = Backoffs[attempt];
        if (JitterFraction <= 0 || baseDelay <= TimeSpan.Zero)
        {
            return baseDelay;
        }

        var jitter = baseDelay.TotalMilliseconds * JitterFraction * Math.Clamp(jitterSample, 0d, 1d);
        return baseDelay + TimeSpan.FromMilliseconds(jitter);
    }
}
