namespace Liakont.Modules.Ingestion.Tests.Unit;

using FluentAssertions;
using Liakont.Modules.Ingestion.Contracts;
using Xunit;

public sealed class AgentContractVersionPolicyTests
{
    [Fact]
    public void Current_Version_Is_Supported()
    {
        AgentContractVersionPolicy.IsSupported(AgentContractVersionPolicy.Current).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("2")]
    [InlineData("v1")]
    [InlineData("inconnu")]
    public void Unknown_Or_Too_Old_Versions_Are_Not_Supported(string? version)
    {
        AgentContractVersionPolicy.IsSupported(version).Should().BeFalse();
    }

    // ── Seam de cohabitation N/N-1 (RDF08) ──────────────────────────────────────────────────────
    // La matrice live a Previous = null (il n'existe pas encore de N-1 en V1), donc la branche N-1
    // de IsSupported n'est jamais exercée par la politique servie. Les tests ci-dessous exercent la
    // décision PURE avec une matrice hypothétique (N="2", N-1="1") pour PROUVER, avant toute rupture
    // réelle, que la plateforme servira simultanément N et N-1 et refusera (426) toute version
    // antérieure à N-1 (ADR-0001/F12 §6.4).

    [Theory]
    [InlineData("2")] // N (courant)
    [InlineData("1")] // N-1 (précédent encore supporté)
    public void Cohabitation_Current_And_Previous_Are_Both_Supported(string version)
    {
        AgentContractVersionPolicy.IsSupported(version, current: "2", previous: "1").Should().BeTrue();
    }

    [Theory]
    [InlineData("0")] // antérieure à N-1
    [InlineData("3")] // postérieure à N (inconnue de la plateforme)
    [InlineData("v1")] // préfixe d'URL ≠ version de contrat (axes distincts)
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Cohabitation_Other_Versions_Are_Refused(string? version)
    {
        AgentContractVersionPolicy.IsSupported(version, current: "2", previous: "1").Should().BeFalse();
    }

    [Fact]
    public void Live_Matrix_Has_No_Previous_So_Only_Current_Is_Served()
    {
        // Garde de non-régression : tant que la V1 est la seule version, Previous reste null et
        // SEULE la version courante est servie (pas de N-1 fantôme).
        AgentContractVersionPolicy.Previous.Should().BeNull();
        AgentContractVersionPolicy.IsSupported(AgentContractVersionPolicy.Current).Should().BeTrue();
        AgentContractVersionPolicy.IsSupported("2").Should().BeFalse();
    }

    [Fact]
    public void Live_IsSupported_Delegates_To_Pure_Decision_With_Live_Matrix()
    {
        // La surcharge live et la décision pure câblée sur la matrice live sont équivalentes : la
        // logique 426 est une SEULE fonction (identique côté plateforme et côté agent).
        foreach (var version in new[] { AgentContractVersionPolicy.Current, "2", "0", "v1", null, string.Empty })
        {
            AgentContractVersionPolicy.IsSupported(version).Should().Be(
                AgentContractVersionPolicy.IsSupported(
                    version, AgentContractVersionPolicy.Current, AgentContractVersionPolicy.Previous));
        }
    }
}
