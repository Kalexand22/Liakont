namespace Liakont.Modules.Pipeline.Contracts.Queries;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Lectures du journal d'exécutions du pipeline (PIP01), TENANT-SCOPÉES PAR CONSTRUCTION : elles
/// s'exécutent sur la base DU TENANT courant (la connexion EST le tenant — database-per-tenant,
/// blueprint §7) ; aucune lecture cross-tenant n'est possible (CLAUDE.md n°9/17). Consommé par
/// <c>GET /runs</c> (API01) et la page Traitements (WEB04). Les exécutions sont écrites par PIP01b+.
/// </summary>
public interface IPipelineRunQueries
{
    /// <summary>
    /// Les exécutions les plus récentes du tenant, triées par début décroissant, bornées par
    /// <paramref name="limit"/> (borné par l'implémentation).
    /// </summary>
    Task<IReadOnlyList<PipelineRunLogDto>> GetRecentRunsAsync(int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Les exécutions du tenant dont le début (<c>started_at</c>) tombe dans l'intervalle de jours
    /// <paramref name="fromInclusive"/>..<paramref name="toInclusive"/> (bornes incluses, chacune
    /// optionnelle), triées par début décroissant et bornées par <paramref name="limit"/> (borné par
    /// l'implémentation). Consommé par <c>GET /runs?from=&amp;to=</c> (API01b).
    /// </summary>
    Task<IReadOnlyList<PipelineRunLogDto>> GetRunsAsync(DateOnly? fromInclusive, DateOnly? toInclusive, int limit, CancellationToken cancellationToken = default);
}
