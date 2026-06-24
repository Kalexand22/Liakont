namespace Liakont.Agent.Adapters.EncheresV6.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Liakont.Agent.Adapters.EncheresV6;
using Liakont.Agent.Adapters.EncheresV6.Tests.Fakes;
using Liakont.Agent.Core;
using Liakont.Agent.Core.Configuration;
using Xunit;

/// <summary>
/// Tests de <see cref="EncheresV6ExtractorFactory"/> (ADP04) sur le NOUVEAU modèle BA/BV : le mode déclaré
/// par la configuration devient un extracteur concret — <see cref="PervasiveExtractor"/> (ODBC) ou
/// <see cref="EncheresV6FixtureExtractor"/> (fixtures) — sans <c>if</c> sur un type ni recompilation
/// (CLAUDE.md n°8). Le n° de dossier (filtre tenant : 1 instance = 1 dossier = 1 tenant) est OBLIGATOIRE
/// en mode ODBC. L'émetteur n'est PLUS porté par l'agent (FilledByPlatform — la plateforme le remplit
/// depuis le profil tenant). La chaîne ODBC est déchiffrée ICI seulement, avec un message français qui ne
/// fuite jamais le secret quand le déchiffrement échoue (CLAUDE.md n°10).
/// </summary>
public class EncheresV6ExtractorFactoryTests
{
    // Snapshot fixtures minimal au NOUVEAU modèle (un BA régime 6 marge) : commission acheteur TTC = 401.28.
    private const string FixtureJson =
        "{ \"regimes\": [], \"bordereaux\": [ { \"no_ba\": \"100022\", \"bordereau_ou_avoir\": \"B\", "
        + "\"date_vente\": \"2024-01-12\", \"nom\": \"Acheteur fictif\", \"total_bordereau\": 401.28, "
        + "\"lignes\": [ { \"type_ligne\": \"1\", \"no_ligne_pv\": \"1\", \"montant_adj_ht\": 2000.0, "
        + "\"montant_frais_ht\": 334.40, \"montant_tva_frais\": 66.88, \"code_regime\": \"6\" } ] } ], "
        + "\"bordereaux_vendeur\": [] }";

    private static readonly DateTime PeriodFrom = new DateTime(2024, 1, 1);
    private static readonly DateTime PeriodTo = new DateTime(2025, 1, 1);

    [Fact]
    public void Pervasive_mode_builds_the_odbc_extractor()
    {
        var protector = new FakeSecretProtector();
        AgentConfig config = Config(
            odbc: protector.Protect("Driver={ODBC Driver 17 for SQL Server};Server=.;Database=EncheresV6_Demo;Trusted_Connection=Yes"),
            fixtures: null,
            section: Section(dossier: "2", schema: "enc"));

        IExtractor extractor = EncheresV6ExtractorFactory.Create(config, protector, new RecordingAgentLog());

        extractor.Should().BeOfType<PervasiveExtractor>();
        extractor.GetInfo().Version.Should().Be("2.0.0-odbc");
    }

    [Fact]
    public void Pervasive_mode_requires_the_dossier_tenant_filter()
    {
        var protector = new FakeSecretProtector();
        AgentConfig config = Config(
            odbc: protector.Protect("DSN=encheresv6;ReadOnly=1"),
            fixtures: null,
            section: Section(dossier: null, schema: "enc"));

        Action act = () => EncheresV6ExtractorFactory.Create(config, protector, new RecordingAgentLog());

        act.Should().Throw<AgentConfigException>().Which.Message
            .Should().Contain("dossier", "le n° de dossier (filtre tenant) est obligatoire en mode ODBC");
    }

    [Fact]
    public void Fixture_mode_builds_the_fixture_extractor()
    {
        string file = Path.Combine(Path.GetTempPath(), "encheresv6-fixture-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(file, FixtureJson);
        try
        {
            AgentConfig config = Config(odbc: null, fixtures: file, section: null);

            IExtractor extractor = EncheresV6ExtractorFactory.Create(config, new FakeSecretProtector(), new RecordingAgentLog());

            extractor.Should().BeOfType<EncheresV6FixtureExtractor>();
            extractor.GetInfo().Version.Should().Be("2.0.0-fixture");
            extractor.ExtractDocuments(PeriodFrom, PeriodTo).Select(d => d.SourceReference)
                .Should().Contain("encheresv6:ba:100022", "le mode fixtures rejoue le snapshot du nouveau modèle");
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Undecryptable_odbc_string_throws_french_error_without_leaking_the_secret()
    {
        var protector = new FakeSecretProtector();
        const string protectedOdbc = "ODBC_CHIFFRE_AILLEURS";
        protector.MarkUndecryptable(protectedOdbc);
        AgentConfig config = Config(odbc: protectedOdbc, fixtures: null, section: Section(dossier: "2", schema: "enc"));

        Action act = () => EncheresV6ExtractorFactory.Create(config, protector, new RecordingAgentLog());

        AgentConfigException ex = act.Should().Throw<AgentConfigException>().Which;
        ex.Message.Should().Contain("déchiffrable");
        ex.Message.Should().NotContain(protectedOdbc, "le message opérateur ne contient jamais le secret (CLAUDE.md n°10)");
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        var protector = new FakeSecretProtector();
        AgentConfig config = Config(odbc: "ODBC_DPAPI_FICTIVE", fixtures: null, section: Section(dossier: "2", schema: null));
        var log = new RecordingAgentLog();

        ((Action)(() => EncheresV6ExtractorFactory.Create(null!, protector, log))).Should().Throw<ArgumentNullException>();
        ((Action)(() => EncheresV6ExtractorFactory.Create(config, null!, log))).Should().Throw<ArgumentNullException>();
        ((Action)(() => EncheresV6ExtractorFactory.Create(config, protector, null!))).Should().Throw<ArgumentNullException>();
    }

    private static AdapterConfigSection Section(string? dossier, string? schema)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (dossier != null)
        {
            values["dossier"] = dossier;
        }

        if (schema != null)
        {
            values["schema"] = schema;
        }

        return new AdapterConfigSection(EncheresV6ExtractorFactory.AdapterName, values);
    }

    private static AgentConfig Config(string? odbc, string? fixtures, AdapterConfigSection? section)
    {
        var extraction = new ExtractionConfig(
            adapter: EncheresV6ExtractorFactory.AdapterName,
            odbcConnectionStringProtected: odbc,
            pdfPoolPath: null,
            schedule: Array.Empty<string>(),
            catchUpOnStart: false,
            fixturesPath: fixtures);

        var adapterConfig = new Dictionary<string, AdapterConfigSection>(StringComparer.OrdinalIgnoreCase);
        if (section != null)
        {
            adapterConfig[section.AdapterName] = section;
        }

        return new AgentConfig(
            platformUrl: "https://liakont.test",
            apiKeyProtected: "APIKEY_DPAPI_FICTIVE",
            extraction: extraction,
            heartbeatMinutes: 15,
            adapterConfig: adapterConfig);
    }
}
