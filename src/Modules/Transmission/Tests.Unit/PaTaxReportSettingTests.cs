namespace Liakont.Modules.Transmission.Tests.Unit;

using System;
using FluentAssertions;
using Liakont.Modules.Transmission.Contracts;
using Xunit;

/// <summary>
/// Couvre la règle d'activation UNIQUE <see cref="PaTaxReportSetting.IsActiveOn"/> (F05 §2 : SIREN publié =
/// date de début renseignée ET non future). Cette règle est consommée par le gating d'envoi (SendTenantJob)
/// ET par l'état de publication de la console (FIX201) — un seul prédicat pour qu'ils ne divergent jamais.
/// </summary>
public sealed class PaTaxReportSettingTests
{
    private static readonly DateOnly Today = new(2026, 6, 11);

    [Fact]
    public void IsActiveOn_Is_False_When_Start_Date_Is_Null()
    {
        new PaTaxReportSetting().IsActiveOn(Today).Should().BeFalse();
    }

    [Fact]
    public void IsActiveOn_Is_True_When_Start_Date_Is_In_The_Past()
    {
        new PaTaxReportSetting { StartDate = new DateOnly(2026, 1, 1) }.IsActiveOn(Today).Should().BeTrue();
    }

    [Fact]
    public void IsActiveOn_Is_True_On_The_Start_Date_Itself()
    {
        new PaTaxReportSetting { StartDate = Today }.IsActiveOn(Today).Should().BeTrue();
    }

    [Fact]
    public void IsActiveOn_Is_False_When_Start_Date_Is_In_The_Future()
    {
        // F05 §2 : une date de début future = SIREN non publié, aucun envoi possible.
        new PaTaxReportSetting { StartDate = new DateOnly(2026, 9, 1) }.IsActiveOn(Today).Should().BeFalse();
    }
}
