namespace Liakont.Modules.Supervision.Tests.Unit.Doubles;

using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Supervision.Application;

/// <summary>Moteur d'évaluation fictif : enregistre le tenant évalué et retourne un bilan configurable.</summary>
internal sealed class RecordingAlertEvaluationService : IAlertEvaluationService
{
    private readonly AlertEvaluationResult _result;

    public RecordingAlertEvaluationService(AlertEvaluationResult? result = null)
    {
        _result = result ?? new AlertEvaluationResult(0, []);
    }

    public string? LastTenantId { get; private set; }

    public int EvaluateCount { get; private set; }

    public Task<AlertEvaluationResult> EvaluateAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        LastTenantId = tenantId;
        EvaluateCount++;
        return Task.FromResult(_result);
    }
}
