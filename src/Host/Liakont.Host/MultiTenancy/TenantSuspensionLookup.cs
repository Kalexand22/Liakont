namespace Liakont.Host.MultiTenancy;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Implémentation de <see cref="ITenantSuspensionLookup"/> : lit le statut du tenant via son
/// SCOPE (<see cref="ITenantScopeFactory"/> → <see cref="ITenantSettingsQueries.GetCurrentTenantStatut"/>,
/// frontière Contracts — jamais de SQL brut cross-base), avec un cache mémoire court (TTL 30 s :
/// la prise d'effet d'une suspension est bornée à ce délai sur les chemins chauds — la console
/// invalide explicitement à la mutation pour un effet immédiat). FAIL-OPEN documenté : une erreur
/// de lecture répond « actif » (et se journalise) — une panne de base ne coupe jamais le push de
/// TOUS les agents ; la suspension est un contrôle opérateur, pas une validation fiscale (les
/// contrôles fiscaux Blocking restent dans le pipeline).
/// </summary>
internal sealed partial class TenantSuspensionLookup : ITenantSuspensionLookup
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private readonly ITenantScopeFactory _scopeFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TenantSuspensionLookup> _logger;

    public TenantSuspensionLookup(
        ITenantScopeFactory scopeFactory,
        IMemoryCache cache,
        ILogger<TenantSuspensionLookup> logger)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _logger = logger;
    }

    public async Task<bool> IsSuspendedAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return false;
        }

        var cacheKey = CacheKey(tenantId);
        if (_cache.TryGetValue(cacheKey, out bool cached))
        {
            return cached;
        }

        bool suspended;
        try
        {
            await using var scope = _scopeFactory.Create(tenantId);
            var queries = scope.Services.GetRequiredService<ITenantSettingsQueries>();
            var statut = await queries.GetCurrentTenantStatut(cancellationToken);

            // Comparaison sur le nom du statut métier (TenantStatus.Suspendu) ; null = pas de
            // profil = actif (un tenant jamais seedé continue d'accepter ses agents).
            suspended = string.Equals(statut, "Suspendu", StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            // FAIL-OPEN : visible en log, jamais avalé en silence — mais on ne coupe pas le tenant
            // sur une panne de LECTURE (le refus reste garanti dès que la lecture revient).
            LogStatusReadFailed(_logger, tenantId, ex);
            return false;
        }

        _cache.Set(cacheKey, suspended, CacheTtl);
        return suspended;
    }

    public void Invalidate(string tenantId) => _cache.Remove(CacheKey(tenantId));

    private static string CacheKey(string tenantId) => $"tenant-suspension:{tenantId}";

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Lecture du statut du tenant '{TenantId}' impossible — réputé ACTIF (fail-open) jusqu'à la prochaine lecture.")]
    private static partial void LogStatusReadFailed(ILogger logger, string tenantId, Exception exception);
}
