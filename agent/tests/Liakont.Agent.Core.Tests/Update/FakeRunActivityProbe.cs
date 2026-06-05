namespace Liakont.Agent.Core.Tests.Update;

using Liakont.Agent.Core.Update;

/// <summary>Sonde de run pilotable.</summary>
internal sealed class FakeRunActivityProbe : IRunActivityProbe
{
    public bool InProgress { get; set; }

    public bool IsRunInProgress() => InProgress;
}
