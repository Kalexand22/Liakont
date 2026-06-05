namespace Liakont.Agent.Adapters.EncheresV6.Tests;

using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Liakont.Agent.Adapters.EncheresV6;
using Liakont.Agent.Adapters.EncheresV6.Tests.Fakes;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Core;
using Liakont.Agent.Core.Configuration;
using Xunit;

/// <summary>
/// Tests de <see cref="EncheresV6ExtractorFactory"/> (ADP04) : le mode déclaré par la configuration
/// devient un extracteur concret — <see cref="PervasiveExtractor"/> (ODBC) ou
/// <see cref="EncheresV6FixtureExtractor"/> (fixtures) — sans <c>if</c> sur un type ni recompilation.
/// La chaîne ODBC est déchiffrée ICI (et nulle part avant), avec un message français qui ne fuite pas
/// le secret quand le déchiffrement échoue (CLAUDE.md n°10).
/// </summary>
public class EncheresV6ExtractorFactoryTests
{
    private static readonly DateTime PeriodFrom = new DateTime(2026, 1, 1);
    private static readonly DateTime PeriodTo = new DateTime(2026, 3, 1);

    private static string FixturesDirectory => Path.Combine(AppContext.BaseDirectory, "fixtures", "encheresv6");

    private static string SalesFile => Path.Combine(FixturesDirectory, "encheresv6-source.json");

    [Fact]
    public void Pervasive_mode_builds_the_odbc_extractor_decrypting_the_connection_string()
    {
        var protector = new FakeSecretProtector();
        EncheresV6AdapterConfig config = EncheresV6AdapterConfig.FromExtractionConfig(
            Extraction(odbc: protector.Protect("DSN=encheresv6;ReadOnly=1"), fixtures: null));

        IExtractor extractor = EncheresV6ExtractorFactory.Create(config, protector, Emitter(), OperationCategory.LivraisonBiens);

        extractor.Should().BeOfType<PervasiveExtractor>();
        extractor.GetInfo().Version.Should().Be("1.0.0-odbc");
    }

    [Fact]
    public void Fixture_mode_builds_the_fixture_extractor_from_a_directory()
    {
        EncheresV6AdapterConfig config = EncheresV6AdapterConfig.FromExtractionConfig(
            Extraction(odbc: null, fixtures: FixturesDirectory));

        IExtractor extractor = EncheresV6ExtractorFactory.Create(config, new FakeSecretProtector(), Emitter(), OperationCategory.LivraisonBiens);

        extractor.Should().BeOfType<EncheresV6FixtureExtractor>();
        extractor.GetInfo().Version.Should().Be("1.0.0-fixture");
        extractor.ExtractDocuments(PeriodFrom, PeriodTo).Should().NotBeEmpty("le mode fixtures rejoue le jeu de données du répertoire");
    }

    [Fact]
    public void Fixture_mode_builds_the_fixture_extractor_from_a_single_file()
    {
        EncheresV6AdapterConfig config = EncheresV6AdapterConfig.FromExtractionConfig(
            Extraction(odbc: null, fixtures: SalesFile));

        IExtractor extractor = EncheresV6ExtractorFactory.Create(config, new FakeSecretProtector(), Emitter(), OperationCategory.LivraisonBiens);

        extractor.Should().BeOfType<EncheresV6FixtureExtractor>();
        extractor.ExtractDocuments(PeriodFrom, PeriodTo).Select(d => d.SourceReference).Should().Contain("no_ba=4500");
    }

    [Fact]
    public void Undecryptable_odbc_string_throws_french_error_without_leaking_the_secret()
    {
        var protector = new FakeSecretProtector();
        const string protectedOdbc = "ODBC_CHIFFRE_AILLEURS";
        protector.MarkUndecryptable(protectedOdbc);
        EncheresV6AdapterConfig config = EncheresV6AdapterConfig.FromExtractionConfig(
            Extraction(odbc: protectedOdbc, fixtures: null));

        Action act = () => EncheresV6ExtractorFactory.Create(config, protector, Emitter(), OperationCategory.LivraisonBiens);

        AgentConfigException ex = act.Should().Throw<AgentConfigException>().Which;
        ex.Message.Should().Contain("déchiffrable");
        ex.Message.Should().NotContain(protectedOdbc, "le message opérateur ne contient jamais le secret (CLAUDE.md n°10)");
    }

    [Fact]
    public void Missing_fixture_file_throws_without_silent_success()
    {
        string absent = Path.Combine(AppContext.BaseDirectory, "fixtures", "introuvable-" + Guid.NewGuid().ToString("N") + ".json");
        EncheresV6AdapterConfig config = EncheresV6AdapterConfig.FromExtractionConfig(
            Extraction(odbc: null, fixtures: absent));

        Action act = () => EncheresV6ExtractorFactory.Create(config, new FakeSecretProtector(), Emitter(), OperationCategory.LivraisonBiens);

        act.Should().Throw<Liakont.Agent.Core.Extraction.SourceSchemaException>();
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        var protector = new FakeSecretProtector();
        EncheresV6AdapterConfig config = EncheresV6AdapterConfig.FromExtractionConfig(
            Extraction(odbc: "ODBC_DPAPI_FICTIVE", fixtures: null));

        ((Action)(() => EncheresV6ExtractorFactory.Create(null!, protector, Emitter(), OperationCategory.LivraisonBiens)))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => EncheresV6ExtractorFactory.Create(config, null!, Emitter(), OperationCategory.LivraisonBiens)))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => EncheresV6ExtractorFactory.Create(config, protector, null!, OperationCategory.LivraisonBiens)))
            .Should().Throw<ArgumentNullException>();
    }

    private static EncheresV6EmitterIdentity Emitter() =>
        new EncheresV6EmitterIdentity(
            name: "Étude Fictïve SVV",
            siren: "111111111",
            city: "Rennes",
            postalCode: "35000",
            countryCode: "FR");

    private static ExtractionConfig Extraction(string? odbc, string? fixtures) =>
        new ExtractionConfig(
            adapter: "EncheresV6",
            odbcConnectionStringProtected: odbc,
            pdfPoolPath: null,
            schedule: Array.Empty<string>(),
            catchUpOnStart: false,
            fixturesPath: fixtures,
            defaultPeriodDays: null);
}
