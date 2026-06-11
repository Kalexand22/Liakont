namespace Liakont.Host.Tests.Unit.Startup;

using FluentAssertions;
using Liakont.Host.Startup;
using Xunit;

public sealed class DevRealmHealthCheckTests
{
    // `expected` est passé en int (la méthode de test est publique, l'enum DevRealmOutcome est
    // internal — un paramètre public ne peut pas exposer un type moins accessible, CS0051) ;
    // les InlineData restent lisibles via (int)DevRealmOutcome.X. Cas :
    //   (admin off | reachable off) → Indeterminate ; reachable + présent → Healthy ;
    //   reachable + absent → Stale (import sauté = realm périmé).
    [Theory]
    [InlineData(false, false, false, (int)DevRealmOutcome.Indeterminate)]
    [InlineData(false, true, true, (int)DevRealmOutcome.Indeterminate)]
    [InlineData(true, false, false, (int)DevRealmOutcome.Indeterminate)]
    [InlineData(true, true, true, (int)DevRealmOutcome.Healthy)]
    [InlineData(true, true, false, (int)DevRealmOutcome.Stale)]
    public void Classify_Should_Map_Probe_Signals_To_Outcome(
        bool adminConfigured, bool realmReachable, bool accountPresent, int expected)
    {
        ((int)DevRealmHealthCheck.Classify(adminConfigured, realmReachable, accountPresent))
            .Should().Be(expected);
    }
}
