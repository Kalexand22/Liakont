namespace Liakont.Agent.Core.Tests.Update;

using Liakont.Agent.Core.Update;

/// <summary>Lanceur d'updater pilotable : capture la requête, renvoie un résultat scriptable.</summary>
internal sealed class FakeUpdaterLauncher : IUpdaterLauncher
{
    public bool LaunchResult { get; set; } = true;

    public UpdaterLaunchRequest? Captured { get; private set; }

    public int LaunchCount { get; private set; }

    public bool Launch(UpdaterLaunchRequest request)
    {
        Captured = request;
        LaunchCount++;
        return LaunchResult;
    }
}
