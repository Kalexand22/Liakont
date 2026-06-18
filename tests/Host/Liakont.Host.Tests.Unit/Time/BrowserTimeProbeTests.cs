namespace Liakont.Host.Tests.Unit.Time;

using System;
using System.Linq;
using Bunit;
using FluentAssertions;
using Liakont.Host.Components;
using Liakont.Host.Time;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// Sonde de fuseau navigateur (RB6) : point d'intégration qui résout le fuseau une fois par circuit puis
/// déclenche le re-rendu des <c>LiakontDate</c>. Couvre la résolution effective via JS et le respect du garde
/// « déjà résolu » (pas de ré-interrogation inutile du navigateur).
/// </summary>
public sealed class BrowserTimeProbeTests : BunitContext
{
    [Fact]
    public void Resolves_The_Browser_Time_Zone_Via_Js_On_Render()
    {
        var tz = new BrowserTimeZone();
        Services.AddSingleton<IBrowserTimeZone>(tz);
        JSInterop.Setup<string?>("liakontTime.getTimeZone").SetResult("Europe/Paris");

        var cut = Render<BrowserTimeProbe>();

        cut.WaitForAssertion(() => tz.IsResolved.Should().BeTrue());
        tz.Zone.Should().NotBeNull();
        JSInterop.VerifyInvoke("liakontTime.getTimeZone");
    }

    [Fact]
    public void Does_Not_Query_The_Browser_When_The_Zone_Is_Already_Resolved()
    {
        // JSInterop en mode STRICT (défaut) + aucun setup : si la sonde appelait le JS alors que le fuseau est
        // déjà résolu, bUnit lèverait. Le garde !IsResolved doit donc l'en empêcher.
        Services.AddSingleton<IBrowserTimeZone>(
            new FakeBrowserTimeZone(TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris")));

        Render<BrowserTimeProbe>();

        JSInterop.Invocations.Where(i => i.Identifier == "liakontTime.getTimeZone").Should().BeEmpty(
            "la sonde ne ré-interroge pas le navigateur quand le fuseau est déjà résolu");
    }
}
