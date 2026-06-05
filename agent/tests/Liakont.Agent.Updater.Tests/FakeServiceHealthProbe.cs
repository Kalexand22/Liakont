namespace Liakont.Agent.Updater.Tests;

using System;

/// <summary>Sonde de santé simulée : renvoie un verdict scriptable, sans attente réelle.</summary>
internal sealed class FakeServiceHealthProbe : IServiceHealthProbe
{
    public bool Healthy { get; set; } = true;

    public bool WaitUntilHealthy(string serviceName, string heartbeatMarkerPath, TimeSpan timeout) => Healthy;
}
