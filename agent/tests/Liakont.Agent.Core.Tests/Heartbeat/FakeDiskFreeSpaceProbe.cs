namespace Liakont.Agent.Core.Tests.Heartbeat;

using Liakont.Agent.Core.Heartbeat;

/// <summary>Sonde disque scriptable : renvoie une valeur fixée (ou <c>null</c> pour simuler une mesure indisponible).</summary>
internal sealed class FakeDiskFreeSpaceProbe : IDiskFreeSpaceProbe
{
    private readonly long? _freeBytes;

    public FakeDiskFreeSpaceProbe(long? freeBytes)
    {
        _freeBytes = freeBytes;
    }

    public long? GetAvailableFreeBytes() => _freeBytes;
}
