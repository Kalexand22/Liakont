namespace Liakont.Agent.Adapters.EncheresV6.Tests;

using System;
using FluentAssertions;
using Liakont.Agent.Adapters.EncheresV6;
using Liakont.Agent.Core.Configuration;
using Xunit;

/// <summary>
/// Validation de <see cref="EncheresV6AdapterConfig"/> (ADP04) : le mode source se tranche par la
/// configuration (jamais par compilation), la règle « chaîne ODBC OU chemin fixtures requis » est
/// EXCLUSIVE, et chaque cas invalide produit un message opérateur FRANÇAIS (CLAUDE.md n°3, n°12).
/// </summary>
public class EncheresV6AdapterConfigTests
{
    [Fact]
    public void Odbc_string_selects_pervasive_mode()
    {
        EncheresV6AdapterConfig config = EncheresV6AdapterConfig.FromExtractionConfig(
            Extraction(odbc: "ODBC_DPAPI_FICTIVE", fixtures: null));

        config.Mode.Should().Be(EncheresV6SourceMode.Pervasive);
        config.OdbcConnectionStringProtected.Should().Be("ODBC_DPAPI_FICTIVE");
        config.FixturesPath.Should().BeNull();
    }

    [Fact]
    public void Fixtures_path_selects_fixture_mode()
    {
        EncheresV6AdapterConfig config = EncheresV6AdapterConfig.FromExtractionConfig(
            Extraction(odbc: null, fixtures: @"D:\Fixtures\encheresv6"));

        config.Mode.Should().Be(EncheresV6SourceMode.Fixture);
        config.FixturesPath.Should().Be(@"D:\Fixtures\encheresv6");
        config.OdbcConnectionStringProtected.Should().BeNull();
    }

    [Fact]
    public void Declaring_both_odbc_and_fixtures_is_rejected_as_ambiguous()
    {
        Action act = () => EncheresV6AdapterConfig.FromExtractionConfig(
            Extraction(odbc: "ODBC_DPAPI_FICTIVE", fixtures: @"D:\Fixtures\encheresv6"));

        act.Should().Throw<AgentConfigException>()
            .Which.Message.Should().Contain("ambigu");
    }

    [Fact]
    public void Declaring_neither_odbc_nor_fixtures_is_rejected()
    {
        Action act = () => EncheresV6AdapterConfig.FromExtractionConfig(
            Extraction(odbc: null, fixtures: null));

        act.Should().Throw<AgentConfigException>()
            .Which.Message.Should().Contain("odbcConnectionString").And.Contain("fixturesPath");
    }

    [Fact]
    public void Default_period_days_is_exposed_as_a_timespan()
    {
        EncheresV6AdapterConfig config = EncheresV6AdapterConfig.FromExtractionConfig(
            Extraction(odbc: "ODBC_DPAPI_FICTIVE", fixtures: null, defaultPeriodDays: 7));

        config.DefaultPeriod.Should().Be(TimeSpan.FromDays(7));
    }

    [Fact]
    public void Default_period_is_null_when_absent()
    {
        EncheresV6AdapterConfig config = EncheresV6AdapterConfig.FromExtractionConfig(
            Extraction(odbc: "ODBC_DPAPI_FICTIVE", fixtures: null));

        config.DefaultPeriod.Should().BeNull("l'agent n'invente aucune fenêtre par défaut (CLAUDE.md n°2)");
    }

    [Fact]
    public void Whitespace_only_fixtures_path_does_not_count_as_a_source()
    {
        // Une valeur blanche n'est pas une source : avec une chaîne ODBC, on reste en mode Pervasive sans ambiguïté.
        EncheresV6AdapterConfig config = EncheresV6AdapterConfig.FromExtractionConfig(
            Extraction(odbc: "ODBC_DPAPI_FICTIVE", fixtures: "   "));

        config.Mode.Should().Be(EncheresV6SourceMode.Pervasive);
        config.FixturesPath.Should().BeNull();
    }

    [Fact]
    public void Null_extraction_throws_argument_null()
    {
        Action act = () => EncheresV6AdapterConfig.FromExtractionConfig(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static ExtractionConfig Extraction(string? odbc, string? fixtures, int? defaultPeriodDays = null) =>
        new ExtractionConfig(
            adapter: "EncheresV6",
            odbcConnectionStringProtected: odbc,
            pdfPoolPath: null,
            schedule: Array.Empty<string>(),
            catchUpOnStart: false,
            fixturesPath: fixtures,
            defaultPeriodDays: defaultPeriodDays);
}
