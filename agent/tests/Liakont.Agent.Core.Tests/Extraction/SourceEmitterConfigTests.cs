namespace Liakont.Agent.Core.Tests.Extraction;

using System;
using System.Collections.Generic;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Core.Configuration;
using Liakont.Agent.Core.Extraction;
using Xunit;

/// <summary>
/// Lecture/validation de l'identité émetteur + nature d'opération depuis <c>adapterConfig.&lt;nom&gt;</c>
/// (ADR-0023) : champ obligatoire manquant ou nature d'opération inconnue → AgentConfigException française.
/// </summary>
public class SourceEmitterConfigTests
{
    [Fact]
    public void FromSection_parses_emitter_and_operation_category()
    {
        AdapterConfigSection section = Section(
            ("emitterSiren", "123456782"),
            ("emitterName", "Société Fictive de Démonstration"),
            ("operationCategory", "livraisonbiens"));

        SourceEmitterConfig config = SourceEmitterConfig.FromSection(section);

        config.EmitterSiren.Should().Be("123456782");
        config.EmitterName.Should().Be("Société Fictive de Démonstration");
        config.OperationCategory.Should().Be(OperationCategory.LivraisonBiens);
    }

    [Fact]
    public void FromSection_reports_missing_required_field()
    {
        AdapterConfigSection section = Section(("emitterName", "X"), ("operationCategory", "Mixte"));

        Action act = () => SourceEmitterConfig.FromSection(section);

        act.Should().Throw<AgentConfigException>().Which.Message.Should().Contain("emitterSiren");
    }

    [Fact]
    public void FromSection_reports_invalid_operation_category()
    {
        AdapterConfigSection section = Section(
            ("emitterSiren", "123456782"),
            ("emitterName", "X"),
            ("operationCategory", "Inconnu"));

        Action act = () => SourceEmitterConfig.FromSection(section);

        act.Should().Throw<AgentConfigException>().Which.Message.Should().Contain("operationCategory");
    }

    private static AdapterConfigSection Section(params (string Key, string Value)[] entries)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, string value) in entries)
        {
            values[key] = value;
        }

        return new AdapterConfigSection("DemoErpA", values);
    }
}
