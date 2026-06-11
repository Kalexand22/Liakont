namespace Liakont.Host.Localization;

using System;
using Microsoft.Extensions.Caching.Memory;

/// <summary>
/// Cache mémoire court de la préférence de langue persistée par utilisateur, pour que
/// <see cref="PersistedLanguageRequestCultureProvider"/> ne lise pas la base à CHAQUE requête.
/// Invalidé par le panneau Préférences à l'enregistrement d'une nouvelle langue (l'utilisateur
/// voit son changement immédiatement, sans attendre l'expiration).
/// </summary>
internal sealed class UserCultureCache(IMemoryCache cache)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    /// <summary>Lit la langue cachée (<c>null</c> caché = utilisateur sans préférence, aussi mémorisé).</summary>
    public bool TryGet(Guid userId, out string? language) => cache.TryGetValue(Key(userId), out language);

    /// <summary>Mémorise la langue persistée (ou son absence) pour la durée du TTL.</summary>
    public void Set(Guid userId, string? language) => cache.Set(Key(userId), language, Ttl);

    /// <summary>Oublie la langue cachée de l'utilisateur (appelé quand il enregistre une nouvelle préférence).</summary>
    public void Invalidate(Guid userId) => cache.Remove(Key(userId));

    private static string Key(Guid userId) => $"liakont:user-language:{userId:N}";
}
