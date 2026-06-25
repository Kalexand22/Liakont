namespace Liakont.Host.Startup;

using Stratum.Modules.Job.Contracts.Services;

/// <summary>
/// Implémentation Liakont de <see cref="ISystemScheduleHost"/> (BUG-4b). L'ensemble des types de jobs SYSTÈME
/// est la source unique <see cref="SystemJobDefinitions"/> (les mêmes fan-out tous-tenants que diagnostique
/// <c>SystemJobScheduleHealthCheck</c> et qu'amorce <c>DevJobScheduleSeeder</c>). Une planification système est
/// portée par <see cref="HostCompanyId"/> — une seule par type de job, plateforme-scopée.
/// </summary>
internal sealed class LiakontSystemScheduleHost : ISystemScheduleHost
{
    /// <summary>
    /// Société porteuse système : sentinel PLATEFORME, PAS un tenant réel (« sched001 »). À l'exécution d'un
    /// fan-out, <c>schedule.CompanyId</c> n'a aucun effet de portée (le runner SOL06 itère tous les tenants
    /// via <c>ITenantQueries</c>) — c'est uniquement une clé d'unicité <c>(name, company)</c> et de
    /// dé-duplication d'enqueue. La MÊME porteuse sert au formulaire d'admin, à la liste cross-tenant ET à
    /// l'amorçage de dev : ainsi une planification système est UNIQUE (pas de double-exécution), et la
    /// maintenance ne croît pas avec le nombre de clients. Distinct du tenant <c>default</c>
    /// (<c>…a000-0001</c>, verrouillé par DefaultCompanyIdCoherenceTests) — il ne désigne aucun tenant.
    /// </summary>
    public static readonly Guid HostCompanyId = Guid.Parse("5c8ed001-0000-4000-b000-000000000001");

    private static readonly HashSet<string> SystemJobTypes =
        new(SystemJobDefinitions.All.Select(j => j.JobType), StringComparer.Ordinal);

    public Guid? CrossTenantHostCompanyId => HostCompanyId;

    public Guid? ResolveHostCompanyId(string jobType) =>
        !string.IsNullOrEmpty(jobType) && SystemJobTypes.Contains(jobType) ? HostCompanyId : null;
}
