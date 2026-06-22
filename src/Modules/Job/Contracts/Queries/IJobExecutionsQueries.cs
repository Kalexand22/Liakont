// Liakont addition (FIX211/FIX210 §4.20/§4.21 catalogue et executions de jobs) - not part of the original Stratum vendoring.
namespace Stratum.Modules.Job.Contracts.Queries;

using Stratum.Modules.Job.Contracts.DTOs;

/// <summary>
/// Lecture des EXÉCUTIONS de jobs (table <c>job.jobs</c>) pour l'admin console : read-model d'administration
/// avec filtres statut / type / période, tenant-scopé par <c>company_id</c> (CLAUDE.md n°9). Distincte de
/// <see cref="IJobQueries"/> (lecture opérationnelle par statut, sans périmètre tenant). Liakont addition (FIX211).
/// </summary>
public interface IJobExecutionsQueries
{
    /// <summary>
    /// Liste les exécutions du tenant (<see cref="JobExecutionsFilter.CompanyId"/>), filtrées et triées par
    /// date de création décroissante, plafonnées à <see cref="JobExecutionsFilter.Limit"/>.
    /// </summary>
    Task<IReadOnlyList<JobDto>> ListAsync(JobExecutionsFilter filter, CancellationToken ct = default);
}

/// <summary>Critères de filtrage de la liste des exécutions de jobs.</summary>
public sealed record JobExecutionsFilter
{
    /// <summary>Tenant courant (company_id) — obligatoire : la requête ne franchit jamais la frontière tenant.</summary>
    public required Guid CompanyId { get; init; }

    /// <summary>Statut exact (Pending / Running / Completed / Failed / Dead), ou <c>null</c> pour tous.</summary>
    public string? Status { get; init; }

    /// <summary>Clé technique du type de job (FullName), ou <c>null</c> pour tous.</summary>
    public string? Type { get; init; }

    /// <summary>Borne basse (inclusive) sur <c>created_at</c>, ou <c>null</c>.</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>Borne haute (inclusive) sur <c>created_at</c>, ou <c>null</c>.</summary>
    public DateTimeOffset? To { get; init; }

    /// <summary>Plafond du nombre de lignes retournées (anti-flood console).</summary>
    public int Limit { get; init; } = 200;
}
