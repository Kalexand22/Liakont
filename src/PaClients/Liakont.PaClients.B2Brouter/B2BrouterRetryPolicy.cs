namespace Liakont.PaClients.B2Brouter;

/// <summary>
/// Politique de retry des erreurs TRANSITOIRES (réseau / 5xx / timeout) de F05 §4.1 : un envoi qui
/// échoue de façon re-tentable est réessayé selon le calendrier de backoff, puis dégradé en
/// <see cref="Modules.Transmission.Contracts.PaSendState.TechnicalError"/> (re-tentable au prochain run).
/// <para>
/// Le calendrier est <b>injectable</b> uniquement pour que les tests unitaires s'exécutent sans
/// attente réelle (backoff zéro) : la valeur de PRODUCTION (<see cref="Default"/>) est figée sur les
/// faits F05 (5 s, 30 s, 2 min), jamais inventée (CLAUDE.md n°2). Le nombre de réessais est la
/// longueur du calendrier ; aucun retry n'est appliqué aux rejets métier (4xx, 200 + <c>errors[]</c>)
/// ni aux erreurs d'authentification (401/403), conformément à F05 §4.1.
/// </para>
/// </summary>
internal sealed record B2BrouterRetryPolicy
{
    /// <summary>
    /// Délais d'attente AVANT chaque réessai, dans l'ordre. La longueur fixe le nombre de réessais :
    /// le nombre total de tentatives d'envoi est <c>Backoffs.Count + 1</c> (la tentative initiale plus
    /// un réessai par délai). Jamais <c>null</c>.
    /// </summary>
    public required IReadOnlyList<TimeSpan> Backoffs { get; init; }

    /// <summary>Nombre de réessais après la tentative initiale (= nombre de délais de backoff).</summary>
    public int RetryCount => Backoffs.Count;

    /// <summary>
    /// Politique de PRODUCTION : 3 réessais, backoff exponentiel 5 s → 30 s → 2 min (F05 §4.1, fait
    /// validé en staging — ne pas re-découvrir, ne pas inventer).
    /// </summary>
    public static B2BrouterRetryPolicy Default { get; } = new()
    {
        Backoffs = [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2)],
    };

    /// <summary>
    /// Politique de TEST : <paramref name="retries"/> réessais sans aucune attente (backoff zéro), pour
    /// exercer la boucle de retry/idempotence sans bloquer la suite unitaire.
    /// </summary>
    /// <param name="retries">Nombre de réessais souhaités (≥ 0).</param>
    public static B2BrouterRetryPolicy NoDelay(int retries = 3)
    {
        if (retries < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(retries), retries, "Le nombre de réessais ne peut être négatif.");
        }

        return new B2BrouterRetryPolicy { Backoffs = Enumerable.Repeat(TimeSpan.Zero, retries).ToArray() };
    }
}
