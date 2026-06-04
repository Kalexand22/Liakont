namespace Liakont.Agent.Core.Tests;

using System;
using Liakont.Agent.Core.Time;

/// <summary>Horloge mutable pour piloter le temps dans les tests (rétention, rotation, fenêtres).</summary>
internal sealed class MutableClock : IClock
{
    private DateTime _utcNow;

    public MutableClock(DateTime utcNow)
    {
        _utcNow = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
    }

    public DateTime UtcNow => _utcNow;

    public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);

    public void Set(DateTime utcNow) => _utcNow = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
}
