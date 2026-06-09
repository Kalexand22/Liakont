namespace Liakont.Modules.Supervision.Tests.Unit.Doubles;

using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Supervision.Application;

/// <summary>Sender de digest factice : capture le tenant pour lequel le digest a été demandé.</summary>
internal sealed class RecordingAlertDigestSender : IAlertDigestSender
{
    public string? LastTenantId { get; private set; }

    public int CallCount { get; private set; }

    public Task SendActiveAlertsDigestAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        LastTenantId = tenantId;
        CallCount++;
        return Task.CompletedTask;
    }
}
