namespace Liakont.Host.Tests.Unit.Startup;

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.Startup;
using Stratum.Common.Infrastructure.Keycloak;
using Xunit;

public sealed class DevRealmHealthCheckTests
{
    private const string Realm = "liakont-dev";

    private static KeycloakAdminOptions ConfiguredAdmin() => new()
    {
        AdminBaseUrl = "http://localhost:8080",
        AdminUsername = "admin",
        AdminPassword = "admin",
        PrimaryRealmName = Realm,
    };

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

    [Fact]
    public async Task RunCheck_Should_Not_Probe_Outside_Development()
    {
        // Garde de sécurité : hors Development, l'API admin Keycloak (donc le mot de passe admin)
        // ne doit JAMAIS être sollicitée. La sonde ne doit pas être appelée.
        var probed = false;
        var outcome = await DevRealmHealthCheck.RunCheckAsync(
            isDevelopment: false,
            admin: ConfiguredAdmin(),
            realmName: Realm,
            probe: (_, _, _) =>
            {
                probed = true;
                return Task.FromResult(true);
            });

        outcome.Should().Be(DevRealmOutcome.Indeterminate);
        probed.Should().BeFalse();
    }

    [Fact]
    public async Task RunCheck_Should_Not_Probe_When_Admin_Not_Configured()
    {
        var probed = false;
        var outcome = await DevRealmHealthCheck.RunCheckAsync(
            isDevelopment: true,
            admin: null,
            realmName: Realm,
            probe: (_, _, _) =>
            {
                probed = true;
                return Task.FromResult(true);
            });

        outcome.Should().Be(DevRealmOutcome.Indeterminate);
        probed.Should().BeFalse();
    }

    [Fact]
    public async Task RunCheck_Should_Be_Healthy_When_Account_Present()
    {
        var outcome = await DevRealmHealthCheck.RunCheckAsync(
            isDevelopment: true,
            admin: ConfiguredAdmin(),
            realmName: Realm,
            probe: (_, _, _) => Task.FromResult(true));

        outcome.Should().Be(DevRealmOutcome.Healthy);
    }

    [Fact]
    public async Task RunCheck_Should_Be_Stale_When_Account_Absent()
    {
        var outcome = await DevRealmHealthCheck.RunCheckAsync(
            isDevelopment: true,
            admin: ConfiguredAdmin(),
            realmName: Realm,
            probe: (_, _, _) => Task.FromResult(false));

        outcome.Should().Be(DevRealmOutcome.Stale);
    }

    [Fact]
    public async Task RunCheck_Should_Be_Indeterminate_When_Probe_Throws()
    {
        // Keycloak injoignable / en démarrage = indéterminé (transitoire), jamais « périmé ».
        var outcome = await DevRealmHealthCheck.RunCheckAsync(
            isDevelopment: true,
            admin: ConfiguredAdmin(),
            realmName: Realm,
            probe: (_, _, _) => throw new InvalidOperationException("keycloak indisponible"));

        outcome.Should().Be(DevRealmOutcome.Indeterminate);
    }
}
