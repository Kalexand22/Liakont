namespace Liakont.Agent.Core.Time;

using System;

/// <summary>Horloge réelle, basée sur l'horloge système en UTC.</summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTime UtcNow => DateTime.UtcNow;
}
