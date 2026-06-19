namespace Liakont.Modules.SupportTrace.Contracts;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Applique la RÉTENTION configurée de la trace de support pour UN tenant (FX06, F16 §7/§10) : calcule la
/// borne (maintenant − rétention) et délègue la suppression au <see cref="ISupportTraceStore"/>. La rétention
/// est un PARAMÉTRAGE (proposition 90 jours, configurable), jamais un seuil fiscal en dur (CLAUDE.md n°2/7).
/// Invoqué par le job de purge planifié, fan-out par tenant via le runner (la cadence relève du déploiement,
/// comme les autres jobs système : aucune cadence inventée). Ne touche NI la piste d'audit append-only NI
/// l'archive probante (le store n'en a aucune connaissance).
/// </summary>
public interface ISupportTracePurgeService
{
    /// <summary>
    /// Purge les traces de support du tenant plus anciennes que la fenêtre de rétention configurée.
    /// </summary>
    /// <param name="tenantId">Le tenant dont on purge les traces expirées.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    /// <returns>Le nombre d'entrées purgées.</returns>
    Task<int> PurgeExpiredAsync(string tenantId, CancellationToken cancellationToken = default);
}
