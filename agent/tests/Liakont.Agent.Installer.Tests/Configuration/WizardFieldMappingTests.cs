namespace Liakont.Agent.Installer.Tests.Configuration;

using System.Linq;
using FluentAssertions;
using Liakont.Agent.Installer.Configuration;
using Liakont.Agent.Installer.Profiles;
using Liakont.Agent.Installer.Wizard;
using Xunit;

/// <summary>
/// Garde anti-faux-vert : tout champ « options » que le wizard collecte DOIT être écrit dans agent.json
/// par <see cref="AgentJsonBuilder"/>. Empêche la régression « champ saisi puis silencieusement perdu »
/// (un champ du registre non porté par le schéma agent.json ne doit pas être offert à la saisie).
/// </summary>
public class WizardFieldMappingTests
{
    [Fact]
    public void Tout_champ_option_du_wizard_est_ecrit_dans_agent_json()
    {
        foreach ((string Key, string Label) option in WizardForm.OptionFields)
        {
            AgentJsonBuilder.MappedFieldKeys.Should().Contain(option.Key);
        }
    }

    [Fact]
    public void Les_champs_non_portes_par_le_schema_ne_sont_pas_collectes()
    {
        string[] collected = WizardForm.OptionFields.Select(o => o.Key).ToArray();

        collected.Should().NotContain(ProfileFieldKeys.Logging);
        collected.Should().NotContain(ProfileFieldKeys.AutoUpdate);
        collected.Should().NotContain(ProfileFieldKeys.OdbcAdvanced);
    }
}
