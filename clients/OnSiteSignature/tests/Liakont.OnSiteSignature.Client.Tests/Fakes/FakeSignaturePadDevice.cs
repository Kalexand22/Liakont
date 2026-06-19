namespace Liakont.OnSiteSignature.Client.Tests.Fakes;

/// <summary>Double de pad renvoyant une capture configurée (aucun SDK Wacom).</summary>
internal sealed class FakeSignaturePadDevice : ISignaturePadDevice
{
    private readonly CapturedSignature _capture;

    public FakeSignaturePadDevice(CapturedSignature capture) => _capture = capture;

    public CapturedSignature Capture() => _capture;
}
