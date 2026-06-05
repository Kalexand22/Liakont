namespace Liakont.Agent.Core.Transport;

using System;

/// <summary>
/// Calcul du délai de backoff EXPONENTIEL appliqué après une réponse 429/5xx/réseau (F12 §3.3) :
/// <c>délai = base × 2^(tentative-1)</c>, plafonné. Pure (aucun effet de bord, aucune horloge) :
/// l'attente réelle est confiée à l'appelant pour rester testable de façon déterministe.
/// </summary>
public sealed class ExponentialBackoff
{
    /// <summary>Délai de base par défaut (première tentative).</summary>
    public static readonly TimeSpan DefaultBaseDelay = TimeSpan.FromSeconds(2);

    /// <summary>Plafond par défaut du délai.</summary>
    public static readonly TimeSpan DefaultMaxDelay = TimeSpan.FromMinutes(5);

    private readonly TimeSpan _baseDelay;
    private readonly TimeSpan _maxDelay;

    /// <summary>Crée un calculateur de backoff.</summary>
    /// <param name="baseDelay">Délai de base (première tentative). Doit être strictement positif.</param>
    /// <param name="maxDelay">Plafond du délai. Doit être supérieur ou égal au délai de base.</param>
    public ExponentialBackoff(TimeSpan baseDelay, TimeSpan maxDelay)
    {
        if (baseDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(baseDelay), "Le délai de base doit être strictement positif.");
        }

        if (maxDelay < baseDelay)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDelay), "Le plafond doit être supérieur ou égal au délai de base.");
        }

        _baseDelay = baseDelay;
        _maxDelay = maxDelay;
    }

    /// <summary>Crée un calculateur de backoff aux valeurs par défaut (2 s → 5 min).</summary>
    public ExponentialBackoff()
        : this(DefaultBaseDelay, DefaultMaxDelay)
    {
    }

    /// <summary>Délai à attendre avant la tentative numéro <paramref name="attempt"/> (1 = première).</summary>
    /// <param name="attempt">Numéro de tentative (≥ 1).</param>
    /// <returns>Le délai, plafonné à la valeur maximale.</returns>
    public TimeSpan DelayFor(int attempt)
    {
        if (attempt < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attempt), "Le numéro de tentative doit être supérieur ou égal à 1.");
        }

        // Exposant borné pour éviter tout dépassement de capacité avant le plafonnement.
        int exponent = Math.Min(attempt - 1, 32);
        double factor = Math.Pow(2, exponent);
        double cappedMs = Math.Min(_baseDelay.TotalMilliseconds * factor, _maxDelay.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(cappedMs);
    }
}
