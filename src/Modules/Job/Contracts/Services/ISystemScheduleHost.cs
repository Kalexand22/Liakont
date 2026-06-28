// Liakont addition (BUG-4b): société porteuse des planifications de jobs SYSTÈME - not part of the original Stratum vendoring.
namespace Stratum.Modules.Job.Contracts.Services;

/// <summary>
/// Résout la société « porteuse » des planifications de jobs SYSTÈME (fan-out plateforme, non rattachés à un
/// tenant). La <c>CompanyId</c> d'une planification système n'a AUCUN effet de portée à l'exécution (le
/// fan-out itère TOUS les tenants, indépendamment de cette valeur) : ce n'est qu'une clé d'unicité
/// <c>(name, company)</c> et de dé-duplication d'enqueue. Cette abstraction permet à un opérateur PLATEFORME
/// (sans société courante) de planifier ET de consulter ces jobs — UNE planification couvre tous les tenants,
/// pour que la maintenance n'explose pas avec le nombre de clients (BUG-4b).
/// </summary>
public interface ISystemScheduleHost
{
    /// <summary>
    /// Société dont les planifications système sont visibles par un opérateur PLATEFORME cross-tenant (sans
    /// société courante) ; <c>null</c> si l'instance n'expose aucun job système (socle nu).
    /// </summary>
    Guid? CrossTenantHostCompanyId { get; }

    /// <summary>
    /// Société porteuse pour un type de job SYSTÈME (fan-out tous tenants) ; <c>null</c> si le type est
    /// tenant-scopé (la planification doit alors être rattachée à la société courante de l'opérateur).
    /// </summary>
    Guid? ResolveHostCompanyId(string jobType);
}
