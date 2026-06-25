// Liakont addition (BUG-4b): défaut no-op de ISystemScheduleHost (socle auto-suffisant) - not part of the original Stratum vendoring.
namespace Stratum.Modules.Job.Infrastructure.Services;

using Stratum.Modules.Job.Contracts.Services;

/// <summary>
/// Défaut no-op de <see cref="ISystemScheduleHost"/> : aucun job n'est traité comme « système » (toute
/// planification reste tenant-scopée) et un opérateur cross-tenant n'a aucune société porteuse à consulter.
/// Préserve le comportement du socle nu ; le Host produit (Liakont) le remplace par une implémentation qui
/// connaît les jobs de fan-out plateforme (BUG-4b).
/// </summary>
internal sealed class NullSystemScheduleHost : ISystemScheduleHost
{
    public Guid? CrossTenantHostCompanyId => null;

    public Guid? ResolveHostCompanyId(string jobType) => null;
}
