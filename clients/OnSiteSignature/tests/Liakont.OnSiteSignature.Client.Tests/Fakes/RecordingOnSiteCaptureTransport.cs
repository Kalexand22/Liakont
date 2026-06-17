namespace Liakont.OnSiteSignature.Client.Tests.Fakes;

using System.Threading;
using System.Threading.Tasks;

/// <summary>Double de transport qui ENREGISTRE le dernier payload transmis (assertions).</summary>
internal sealed class RecordingOnSiteCaptureTransport : IOnSiteCaptureTransport
{
    public OnSiteCapturePayload? LastPayload { get; private set; }

    public Task SendAsync(OnSiteCapturePayload payload, CancellationToken cancellationToken)
    {
        LastPayload = payload;
        return Task.CompletedTask;
    }
}
