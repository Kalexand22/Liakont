namespace Liakont.Modules.Supervision.Infrastructure;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Supervision.Contracts;
using Stratum.Modules.Job.Contracts.Queries;

/// <summary>
/// Lecture de la dernière évaluation du dead-man's-switch (FIX210, F12 §5.1). L'évaluation est un job SYSTÈME
/// (<see cref="SupervisionEvaluationTrigger"/>, fan-out cross-tenant in-process) : ses exécutions vivent dans
/// la base SYSTÈME. On lit le dernier achèvement de ce type via le Contract du module Job
/// (<see cref="IJobQueries.GetLastCompletedAtByTypeAsync"/>, filtré en SQL — pas de scan plafonné). L'appelant
/// (témoin de vie côté Host) résout ce service dans un scope SYSTÈME, ce qui route la connexion vers la base système.
/// </summary>
public sealed class SupervisionLivenessQueries : ISupervisionLivenessQueries
{
    private static readonly string EvaluationJobType = typeof(SupervisionEvaluationTrigger).FullName!;

    private readonly IJobQueries _jobQueries;

    public SupervisionLivenessQueries(IJobQueries jobQueries)
    {
        ArgumentNullException.ThrowIfNull(jobQueries);
        _jobQueries = jobQueries;
    }

    public Task<DateTimeOffset?> GetLastEvaluationUtcAsync(CancellationToken cancellationToken = default) =>
        _jobQueries.GetLastCompletedAtByTypeAsync(EvaluationJobType, cancellationToken);
}
