namespace Liakont.Host.Tests.Unit.Time;

using System;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.UI.Components;
using Stratum.Common.UI.Time;
using Xunit;

/// <summary>
/// Composant d'affichage de date au fuseau navigateur (RB6) : conversion locale une fois le fuseau résolu,
/// repli UTC explicite avant résolution (anti-mensonge), re-rendu sur l'événement Resolved, et NullText.
/// </summary>
public sealed class LiakontDateTests : BunitContext
{
    private static readonly TimeZoneInfo Paris = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");
    private static readonly DateTimeOffset Utc = new(2026, 6, 17, 16, 42, 0, TimeSpan.Zero);

    [Fact]
    public void Renders_Local_Time_When_The_Browser_Zone_Is_Resolved()
    {
        Services.AddSingleton<IBrowserTimeZone>(new FakeBrowserTimeZone(Paris));

        var cut = Render<LiakontDate>(p => p.Add(c => c.Value, Utc));

        cut.Markup.Trim().Should().Be("17/06/2026 18:42");
    }

    [Fact]
    public void Renders_Explicit_Utc_Before_The_Browser_Zone_Is_Resolved()
    {
        // Pré-rendu : jamais une fausse heure locale → UTC suffixé.
        Services.AddSingleton<IBrowserTimeZone>(new FakeBrowserTimeZone(zone: null));

        var cut = Render<LiakontDate>(p => p.Add(c => c.Value, Utc));

        cut.Markup.Trim().Should().Be("17/06/2026 16:42 UTC");
    }

    [Fact]
    public void Date_Only_Uses_The_Browser_Zone_For_The_Calendar_Day()
    {
        // 23:30 UTC → 01:30 le lendemain à Paris (été) : la date affichée bascule au jour suivant.
        Services.AddSingleton<IBrowserTimeZone>(new FakeBrowserTimeZone(Paris));

        var cut = Render<LiakontDate>(p => p
            .Add(c => c.Value, new DateTimeOffset(2026, 6, 17, 23, 30, 0, TimeSpan.Zero))
            .Add(c => c.DateOnly, true));

        cut.Markup.Trim().Should().Be("18/06/2026");
    }

    [Fact]
    public void Null_Value_Renders_The_Custom_NullText()
    {
        Services.AddSingleton<IBrowserTimeZone>(new FakeBrowserTimeZone(Paris));

        var cut = Render<LiakontDate>(p => p
            .Add(c => c.Value, (DateTimeOffset?)null)
            .Add(c => c.NullText, "jamais"));

        cut.Markup.Trim().Should().Be("jamais");
    }

    [Fact]
    public void Re_Renders_In_Local_Time_When_The_Zone_Resolves_After_First_Render()
    {
        // Le vrai chemin : 1er rendu sans fuseau (UTC suffixé) → la sonde résout → re-rendu en local.
        var tz = new FakeBrowserTimeZone(zone: null);
        Services.AddSingleton<IBrowserTimeZone>(tz);

        var cut = Render<LiakontDate>(p => p.Add(c => c.Value, Utc));
        cut.Markup.Trim().Should().Be("17/06/2026 16:42 UTC", "pré-rendu : UTC explicite, jamais d'heure locale fausse");

        cut.InvokeAsync(() => tz.ResolveTo(Paris));

        cut.WaitForAssertion(() => cut.Markup.Trim().Should().Be("17/06/2026 18:42"));
    }
}
