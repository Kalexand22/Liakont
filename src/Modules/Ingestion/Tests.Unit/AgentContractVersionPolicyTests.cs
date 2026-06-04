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
}
