namespace Liakont.Agent.Updater.Tests;

using System;

/// <summary>Pilotage de service simulé : compte les arrêts/démarrages.</summary>
internal sealed class FakeServiceControl : IServiceControl
{
    public int StopCount { get; private set; }

    public int StartCount { get; private set; }

    public void StopService(string serviceName, TimeSpan timeout) => StopCount++;

    public void StartService(string serviceName, TimeSpan timeout) => StartCount++;
}
