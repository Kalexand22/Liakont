namespace Liakont.Modules.Supervision.Tests.Unit.Doubles;

using System;

/// <summary>Horloge de test figée — rend l'évaluation et les horodatages déterministes.</summary>
internal sealed class FixedTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public FixedTimeProvider(DateTimeOffset now) => _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>Avance l'horloge (pour simuler un cycle d'évaluation ultérieur).</summary>
    public void Advance(TimeSpan delta) => _now = _now.Add(delta);
}
