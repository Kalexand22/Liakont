namespace Liakont.Agent.Installer.Tests.Configuration;

using System;
using System.Collections.Generic;
using FluentAssertions;
using Liakont.Agent.Core.Configuration;
using Liakont.Agent.Installer.Configuration;
using Liakont.Agent.Installer.Profiles;
using Liakont.Agent.Installer.Tests.Fakes;
using Xunit;

/// <summary>
/// Garde de la construction de agent.json (F13 §4/§6) : secrets CHIFFRÉS DPAPI, jamais en clair ; schéma
/// re-validé par le cœur agent (anti-dérive). « Écriture agent.json avec secrets chiffrés DPAPI — vérifié ».
/// </summary>
public class AgentJsonBuilderTests
{
    [Fact]
    public void Build_chiffre_la_cle_api_et_l_odbc_et_ne_les_laisse_jamais_en_clair()
    {
        var protector = new FakeSecretProtector();
        ResolvedConfiguration config = ResolvedConfig(
            (ProfileFieldKeys.PlatformUrl, "https://liakont.exemple.fr"),
            (ProfileFieldKeys.ApiKey, "pk_clair_secret"),
            (ProfileFieldKeys.Adapter, "EncheresV6"),
            (ProfileFieldKeys.OdbcConnection, "DSN=Source;Uid=u;Pwd=motdepasse"));

        string json = AgentJsonBuilder.Build(config, protector);

        json.Should().Contain(protector.Protect("pk_clair_secret"));
        json.Should().Contain(protector.Protect("DSN=Source;Uid=u;Pwd=motdepasse"));
        json.Should().NotContain("pk_clair_secret");
        json.Should().NotContain("Pwd=motdepasse");
    }

    [Fact]
    public void Build_decoupe_la_planification_en_heures()
    {
        ResolvedConfiguration config = ResolvedConfig(
            (ProfileFieldKeys.PlatformUrl, "https://liakont.exemple.fr"),
            (ProfileFieldKeys.ApiKey, "pk"),
            (ProfileFieldKeys.Adapter, "EncheresV6"),
            (ProfileFieldKeys.Schedule, "03:00,13:00"));

        string json = AgentJsonBuilder.Build(config, new FakeSecretProtector());

        json.Should().Contain("03:00");
        json.Should().Contain("13:00");
    }

    [Fact]
    public void Build_rejette_une_url_de_plateforme_non_https()
    {
        ResolvedConfiguration config = ResolvedConfig(
            (ProfileFieldKeys.PlatformUrl, "http://insecure.example.fr"),
            (ProfileFieldKeys.ApiKey, "pk"),
            (ProfileFieldKeys.Adapter, "EncheresV6"));

        Action act = () => AgentJsonBuilder.Build(config, new FakeSecretProtector());

        act.Should().Throw<AgentConfigException>();
    }

    [Fact]
    public void Build_omet_la_chaine_odbc_quand_elle_est_absente()
    {
        ResolvedConfiguration config = ResolvedConfig(
            (ProfileFieldKeys.PlatformUrl, "https://liakont.exemple.fr"),
            (ProfileFieldKeys.ApiKey, "pk"),
            (ProfileFieldKeys.Adapter, "EncheresV6"));

        string json = AgentJsonBuilder.Build(config, new FakeSecretProtector());

        json.Should().NotContain("odbcConnectionString");
    }

    [Fact]
    public void Build_ecrit_la_date_de_debut_quand_renseignee()
    {
        ResolvedConfiguration config = ResolvedConfig(
            (ProfileFieldKeys.PlatformUrl, "https://liakont.exemple.fr"),
            (ProfileFieldKeys.ApiKey, "pk"),
            (ProfileFieldKeys.Adapter, "EncheresV6"),
            (ProfileFieldKeys.ExtractFromUtc, "2026-01-01"));

        string json = AgentJsonBuilder.Build(config, new FakeSecretProtector());

        json.Should().Contain("extractFromUtc").And.Contain("2026-01-01");
    }

    [Fact]
    public void Build_omet_la_date_de_debut_quand_absente()
    {
        ResolvedConfiguration config = ResolvedConfig(
            (ProfileFieldKeys.PlatformUrl, "https://liakont.exemple.fr"),
            (ProfileFieldKeys.ApiKey, "pk"),
            (ProfileFieldKeys.Adapter, "EncheresV6"));

        string json = AgentJsonBuilder.Build(config, new FakeSecretProtector());

        json.Should().NotContain("extractFromUtc", "vide → uniquement les nouveaux documents, aucune borne d'historique");
    }

    private static ResolvedConfiguration ResolvedConfig(params (string Key, string? Value)[] values)
    {
        var dict = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach ((string Key, string? Value) pair in values)
        {
            dict[pair.Key] = pair.Value;
        }

        return new ResolvedConfiguration(dict);
    }
}
