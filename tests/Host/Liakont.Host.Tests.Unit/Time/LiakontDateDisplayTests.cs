namespace Liakont.Host.Tests.Unit.Time;

using System;
using FluentAssertions;
using Stratum.Common.UI.Time;
using Xunit;

/// <summary>
/// Formatage commun des dates (RB6) : conversion UTC → fuseau navigateur, repli UTC explicite quand le fuseau
/// n'est pas (encore) résolu, et culture fr-FR. Le serveur tourne en UTC : c'est ce helper, et non
/// <c>ToLocalTime()</c> côté serveur, qui rend l'heure du lecteur.
/// </summary>
public sealed class LiakontDateDisplayTests
{
    private static readonly TimeZoneInfo Paris = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");

    [Fact]
    public void DateTime_Converts_Utc_To_Browser_Zone_In_Summer_Dst()
    {
        // Été (CEST = UTC+2) : 16:42 UTC → 18:42 à Paris, sans suffixe (l'heure est désormais locale au lecteur).
        var utc = new DateTimeOffset(2026, 6, 17, 16, 42, 0, TimeSpan.Zero);

        LiakontDateDisplay.DateTime(utc, Paris).Should().Be("17/06/2026 18:42");
    }

    [Fact]
    public void DateTime_Converts_Utc_To_Browser_Zone_In_Winter_No_Dst()
    {
        // Hiver (CET = UTC+1) : 16:42 UTC → 17:42 à Paris. Vérifie la prise en compte de l'heure d'été/hiver.
        var utc = new DateTimeOffset(2026, 1, 17, 16, 42, 0, TimeSpan.Zero);

        LiakontDateDisplay.DateTime(utc, Paris).Should().Be("17/01/2026 17:42");
    }

    [Fact]
    public void DateTime_With_Unresolved_Zone_Falls_Back_To_Explicit_Utc_Never_A_False_Local_Time()
    {
        // Pré-rendu : fuseau inconnu → UTC EXPLICITEMENT suffixé (jamais une heure locale ambiguë).
        var utc = new DateTimeOffset(2026, 6, 17, 16, 42, 0, TimeSpan.Zero);

        LiakontDateDisplay.DateTime(utc, zone: null).Should().Be("17/06/2026 16:42 UTC");
    }

    [Fact]
    public void DateTime_Normalises_A_Non_Utc_Offset_Before_Converting()
    {
        // Une source portant déjà un offset (ex. +05:00) est ramenée à l'instant absolu puis convertie au
        // fuseau navigateur — pas d'addition d'offsets. 16:42+05:00 = 11:42 UTC = 13:42 à Paris (été).
        var withOffset = new DateTimeOffset(2026, 6, 17, 16, 42, 0, TimeSpan.FromHours(5));

        LiakontDateDisplay.DateTime(withOffset, Paris).Should().Be("17/06/2026 13:42");
    }

    [Fact]
    public void Date_Only_Uses_The_Browser_Zone_For_The_Calendar_Day()
    {
        // 23:30 UTC le 17/06 = 01:30 le 18/06 à Paris (été) → la date AFFICHÉE bascule au 18.
        var utc = new DateTimeOffset(2026, 6, 17, 23, 30, 0, TimeSpan.Zero);

        LiakontDateDisplay.Date(utc, Paris).Should().Be("18/06/2026");
        LiakontDateDisplay.Date(utc, zone: null).Should().Be("17/06/2026 UTC");
    }

    [Fact]
    public void Null_Value_Renders_The_Placeholder()
    {
        LiakontDateDisplay.DateTime(null, Paris).Should().Be(LiakontDateDisplay.Placeholder);
        LiakontDateDisplay.Date(null, zone: null).Should().Be(LiakontDateDisplay.Placeholder);
    }
}
