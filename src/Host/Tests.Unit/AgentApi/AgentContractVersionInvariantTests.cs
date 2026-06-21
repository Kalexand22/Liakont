namespace Liakont.Host.Tests.Unit.AgentApi;

using FluentAssertions;
using Liakont.Agent.Contracts;
using Xunit;

/// <summary>
/// RDL04 — invariant statique liant les DEUX constantes de version du contrat (finding A7-evo-4) :
/// <see cref="AgentContractVersion.Current"/> (préfixe d'URL, ex. <c>"v1"</c>) et
/// <see cref="AgentContractVersion.ContractVersion"/> (version du payload, ex. <c>"1"</c>) étaient deux
/// littéraux indépendants jamais croisés — au passage v2 il fallait muter à la main 3 sources de vérité
/// (const URL, const payload, préfixe MapGroup). Le préfixe MapGroup est désormais DÉRIVÉ de
/// <c>Current</c> (<c>AgentApiEndpoints.MapAgentApi</c>) ; ce test verrouille la dernière liberté : que
/// <c>Current</c> et <c>ContractVersion</c> restent cohérentes (<c>Current == "v" + ContractVersion</c>).
/// Le runbook de bascule v2 (contrat-agent-v1.md §4) liste les points à muter ENSEMBLE.
/// </summary>
public sealed class AgentContractVersionInvariantTests
{
    [Fact]
    public void Current_url_prefix_is_derived_from_contract_version()
    {
        // Si v2 ne mute qu'une des deux constantes, ce test casse — empêchant un préfixe d'URL
        // (/api/agent/vN) désaligné de la version de payload réellement portée par l'assembly.
        AgentContractVersion.Current.Should().Be(
            "v" + AgentContractVersion.ContractVersion,
            "le préfixe d'URL et la version de payload sont une seule vérité (RDL04)");
    }

    [Fact]
    public void Contract_version_is_a_plain_positive_integer()
    {
        // ContractVersion est un entier nu ("1", "2"…) : la dérivation "v" + ContractVersion ne produit
        // un préfixe d'URL valide que sous cette forme. Garde le runbook v2 honnête.
        int.TryParse(AgentContractVersion.ContractVersion, out int version).Should().BeTrue(
            "la version de payload est un entier nu");
        version.Should().BeGreaterThan(0);
    }
}
