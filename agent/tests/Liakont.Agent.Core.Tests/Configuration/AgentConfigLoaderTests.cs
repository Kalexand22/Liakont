namespace Liakont.Agent.Core.Tests.Configuration;

using System;
using System.IO;
using FluentAssertions;
using Liakont.Agent.Core.Configuration;
using Xunit;

/// <summary>
/// Validation de <c>agent.json</c> (F12 §2.4) : cas valide complet, valeurs par défaut, et chaque cas
/// invalide produit un message opérateur FRANÇAIS nommant le champ (CLAUDE.md n°3 « bloquer plutôt
/// qu'accepter faux », n°12 « messages français »).
/// </summary>
public class AgentConfigLoaderTests
{
    private const string ValidJson = @"{
  ""platformUrl"": ""https://liakont.editeur-x.fr"",
  ""apiKey"": ""APIKEY_DPAPI_FICTIVE"",
  ""extraction"": {
    ""adapter"": ""EncheresV6"",
    ""odbcConnectionString"": ""ODBC_DPAPI_FICTIVE"",
    ""pdfPoolPath"": ""D:\\Ventes\\PDF"",
    ""schedule"": [""03:00"", ""18:30""],
    ""catchUpOnStart"": true
  },
  ""heartbeatMinutes"": 30
}";

    [Fact]
    public void Valid_config_is_parsed_with_all_fields()
    {
        AgentConfig config = AgentConfigLoader.Parse(ValidJson, "agent.json");

        config.PlatformUrl.Should().Be("https://liakont.editeur-x.fr");
        config.ApiKeyProtected.Should().Be("APIKEY_DPAPI_FICTIVE");
        config.HeartbeatMinutes.Should().Be(30);
        config.Extraction.Adapter.Should().Be("EncheresV6");
        config.Extraction.OdbcConnectionStringProtected.Should().Be("ODBC_DPAPI_FICTIVE");
        config.Extraction.PdfPoolPath.Should().Be(@"D:\Ventes\PDF");
        config.Extraction.Schedule.Should().Equal("03:00", "18:30");
        config.Extraction.CatchUpOnStart.Should().BeTrue();
    }

    [Fact]
    public void Heartbeat_defaults_to_15_when_absent()
    {
        const string json = @"{ ""platformUrl"": ""https://x.fr"", ""apiKey"": ""k"", ""extraction"": { ""adapter"": ""Fixture"" } }";

        AgentConfig config = AgentConfigLoader.Parse(json, "agent.json");

        config.HeartbeatMinutes.Should().Be(AgentConfigLoader.DefaultHeartbeatMinutes);
        config.Extraction.OdbcConnectionStringProtected.Should().BeNull();
        config.Extraction.Schedule.Should().BeEmpty();
        config.Extraction.CatchUpOnStart.Should().BeFalse();
        config.Extraction.FixturesPath.Should().BeNull();
    }

    [Fact]
    public void Fixtures_path_is_parsed()
    {
        const string json = @"{ ""platformUrl"": ""https://x.fr"", ""apiKey"": ""k"", ""extraction"": { ""adapter"": ""EncheresV6"", ""fixturesPath"": ""D:\\Fixtures\\encheresv6"" } }";

        AgentConfig config = AgentConfigLoader.Parse(json, "agent.json");

        config.Extraction.FixturesPath.Should().Be(@"D:\Fixtures\encheresv6");
    }

    [Fact]
    public void Missing_file_throws_with_french_message()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");

        Action act = () => AgentConfigLoader.Load(path);

        act.Should().Throw<AgentConfigException>()
            .Which.Message.Should().Contain("introuvable");
    }

    [Fact]
    public void Invalid_json_throws_with_french_message()
    {
        Action act = () => AgentConfigLoader.Parse("{ ceci n'est pas du json", "agent.json");

        act.Should().Throw<AgentConfigException>()
            .Which.Message.Should().Contain("JSON valide");
    }

    [Fact]
    public void Missing_platform_url_is_reported()
    {
        const string json = @"{ ""apiKey"": ""k"", ""extraction"": { ""adapter"": ""Fixture"" } }";

        Action act = () => AgentConfigLoader.Parse(json, "agent.json");

        act.Should().Throw<AgentConfigException>()
            .Which.Errors.Should().Contain(e => e.Contains("platformUrl"));
    }

    [Fact]
    public void Non_absolute_platform_url_is_reported()
    {
        const string json = @"{ ""platformUrl"": ""liakont"", ""apiKey"": ""k"", ""extraction"": { ""adapter"": ""Fixture"" } }";

        Action act = () => AgentConfigLoader.Parse(json, "agent.json");

        act.Should().Throw<AgentConfigException>()
            .Which.Errors.Should().Contain(e => e.Contains("platformUrl"));
    }

    [Fact]
    public void Cleartext_http_platform_url_is_rejected()
    {
        const string json = @"{ ""platformUrl"": ""http://liakont.editeur-x.fr"", ""apiKey"": ""k"", ""extraction"": { ""adapter"": ""Fixture"" } }";

        Action act = () => AgentConfigLoader.Parse(json, "agent.json");

        act.Should().Throw<AgentConfigException>("http en clair exposerait la clé API et les données fiscales (F12 §2.6)")
            .Which.Errors.Should().Contain(e => e.Contains("platformUrl"));
    }

    [Fact]
    public void Loopback_http_platform_url_is_accepted_for_diagnostics()
    {
        const string json = @"{ ""platformUrl"": ""http://localhost:5000"", ""apiKey"": ""k"", ""extraction"": { ""adapter"": ""Fixture"" } }";

        AgentConfig config = AgentConfigLoader.Parse(json, "agent.json");

        config.PlatformUrl.Should().Be("http://localhost:5000");
    }

    [Fact]
    public void Missing_api_key_is_reported()
    {
        const string json = @"{ ""platformUrl"": ""https://x.fr"", ""extraction"": { ""adapter"": ""Fixture"" } }";

        Action act = () => AgentConfigLoader.Parse(json, "agent.json");

        act.Should().Throw<AgentConfigException>()
            .Which.Errors.Should().Contain(e => e.Contains("apiKey"));
    }

    [Fact]
    public void Missing_extraction_section_is_reported()
    {
        const string json = @"{ ""platformUrl"": ""https://x.fr"", ""apiKey"": ""k"" }";

        Action act = () => AgentConfigLoader.Parse(json, "agent.json");

        act.Should().Throw<AgentConfigException>()
            .Which.Errors.Should().Contain(e => e.Contains("extraction"));
    }

    [Fact]
    public void Missing_adapter_is_reported()
    {
        const string json = @"{ ""platformUrl"": ""https://x.fr"", ""apiKey"": ""k"", ""extraction"": { } }";

        Action act = () => AgentConfigLoader.Parse(json, "agent.json");

        act.Should().Throw<AgentConfigException>()
            .Which.Errors.Should().Contain(e => e.Contains("extraction.adapter"));
    }

    [Fact]
    public void Invalid_schedule_entry_is_reported()
    {
        const string json = @"{ ""platformUrl"": ""https://x.fr"", ""apiKey"": ""k"", ""extraction"": { ""adapter"": ""Fixture"", ""schedule"": [""3h00"", ""25:99""] } }";

        Action act = () => AgentConfigLoader.Parse(json, "agent.json");

        act.Should().Throw<AgentConfigException>()
            .Which.Errors.Should().Contain(e => e.Contains("schedule"));
    }

    [Fact]
    public void Non_positive_heartbeat_is_reported()
    {
        const string json = @"{ ""platformUrl"": ""https://x.fr"", ""apiKey"": ""k"", ""extraction"": { ""adapter"": ""Fixture"" }, ""heartbeatMinutes"": 0 }";

        Action act = () => AgentConfigLoader.Parse(json, "agent.json");

        act.Should().Throw<AgentConfigException>()
            .Which.Errors.Should().Contain(e => e.Contains("heartbeatMinutes"));
    }

    [Fact]
    public void Multiple_errors_are_collected_together()
    {
        const string json = @"{ ""extraction"": { } }";

        Action act = () => AgentConfigLoader.Parse(json, "agent.json");

        act.Should().Throw<AgentConfigException>()
            .Which.Errors.Count.Should().BeGreaterThan(1);
    }

    [Fact]
    public void Adapter_config_section_is_parsed_and_keys_are_case_insensitive()
    {
        const string json = @"{
  ""platformUrl"": ""https://x.fr"",
  ""apiKey"": ""k"",
  ""extraction"": { ""adapter"": ""DemoErpA"" },
  ""adapterConfig"": {
    ""DemoErpA"": { ""emitterSiren"": ""123456782"", ""operationCategory"": ""LivraisonBiens"" }
  }
}";

        AgentConfig config = AgentConfigLoader.Parse(json, "agent.json");

        AdapterConfigSection section = config.GetAdapterConfig("DemoErpA");
        section.GetRequired("emitterSiren").Should().Be("123456782");
        section.GetOptional("OPERATIONCATEGORY").Should().Be("LivraisonBiens");
        section.GetOptional("inconnu").Should().BeNull();
    }

    [Fact]
    public void Adapter_config_section_is_resolved_by_name_case_insensitively()
    {
        const string json = @"{ ""platformUrl"": ""https://x.fr"", ""apiKey"": ""k"", ""extraction"": { ""adapter"": ""DemoErpB"" }, ""adapterConfig"": { ""DemoErpB"": { ""emitterSiren"": ""123456782"" } } }";

        AgentConfig config = AgentConfigLoader.Parse(json, "agent.json");

        config.GetAdapterConfig("demoerpb").GetRequired("emitterSiren").Should().Be("123456782");
    }

    [Fact]
    public void Missing_adapter_config_section_yields_an_empty_section()
    {
        // ValidJson ne porte aucun bloc adapterConfig : la section doit être vide, pas nulle.
        AgentConfig config = AgentConfigLoader.Parse(ValidJson, "agent.json");

        config.GetAdapterConfig("EncheresV6").GetOptional("emitterSiren").Should().BeNull();
    }

    [Fact]
    public void Required_adapter_config_value_throws_french_message_when_absent()
    {
        AgentConfig config = AgentConfigLoader.Parse(ValidJson, "agent.json");

        Action act = () => config.GetAdapterConfig("EncheresV6").GetRequired("emitterSiren");

        act.Should().Throw<AgentConfigException>()
            .Which.Message.Should().Contain("adapterConfig.EncheresV6.emitterSiren");
    }

    [Fact]
    public void Extract_from_utc_is_parsed_when_present()
    {
        const string json = @"{ ""platformUrl"": ""https://x.fr"", ""apiKey"": ""k"", ""extraction"": { ""adapter"": ""DemoErpA"", ""extractFromUtc"": ""2026-01-01T00:00:00Z"" } }";

        AgentConfig config = AgentConfigLoader.Parse(json, "agent.json");

        config.Extraction.ExtractFromUtc.Should().Be(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Invalid_extract_from_utc_is_reported()
    {
        const string json = @"{ ""platformUrl"": ""https://x.fr"", ""apiKey"": ""k"", ""extraction"": { ""adapter"": ""DemoErpA"", ""extractFromUtc"": ""pas-une-date"" } }";

        Action act = () => AgentConfigLoader.Parse(json, "agent.json");

        act.Should().Throw<AgentConfigException>().Which.Errors.Should().Contain(e => e.Contains("extractFromUtc"));
    }
}
