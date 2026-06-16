namespace Liakont.Modules.SupportTrace.Infrastructure;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.SupportTrace.Contracts;
using Microsoft.Extensions.Options;

/// <summary>
/// Applique la rétention configurée de la trace de support pour UN tenant (FX06, F16 §7/§10) : la borne est
/// <c>maintenant − RetentionDays</c> (horloge injectée pour un test déterministe), déléguée au
/// <see cref="ISupportTraceStore"/>. La rétention est un PARAMÉTRAGE (proposition 90 jours, configurable),
/// jamais un seuil fiscal en dur (CLAUDE.md n°2/7). Garde de sûreté : une rétention non positive est REFUSÉE
/// (elle purgerait tout, ou pire) — le module pose déjà un repli au défaut, ceci ferme un mauvais paramétrage.
/// </summary>
internal sealed class SupportTracePurgeService : ISupportTracePurgeService
{
    private readonly ISupportTraceStore _store;
    private readonly IOptions<SupportTraceOptions> _options;
    private readonly TimeProvider _clock;

    public SupportTracePurgeService(ISupportTraceStore store, IOptions<SupportTraceOptions> options, TimeProvider clock)
    {
        _store = store;
        _options = options;
        _clock = clock;
    }

    /// <inheritdoc />
    public Task<int> PurgeExpiredAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        int retentionDays = _options.Value.RetentionDays;
        if (retentionDays <= 0)
        {
            throw new InvalidOperationException(
                $"La rétention de la trace de support doit être strictement positive (SupportTrace:RetentionDays = {retentionDays}).");
        }

        DateTimeOffset cutoff = _clock.GetUtcNow() - TimeSpan.FromDays(retentionDays);
        return _store.PurgeOlderThanAsync(tenantId, cutoff, cancellationToken);
    }
}
