namespace Liakont.Modules.FleetSupervision.Tests.Unit;

using FluentAssertions;
using Liakont.Modules.FleetSupervision.Application;
using Xunit;

/// <summary>
/// Comparaison de versions (OPS04) : on extrait le cœur sémantique de l'<c>AssemblyInformationalVersion</c>
/// (forme <c>x.y.z+hash</c>) et on est CONSERVATEUR — une version illisible ne déclenche jamais l'alerte.
/// </summary>
public sealed class FleetVersionTests
{
    [Theory]
    [InlineData("1.4.0", "1.4.0")]
    [InlineData("1.4.0+9e0917c", "1.4.0")]
    [InlineData("1.4.0-rc1", "1.4.0")]
    [InlineData(" 1.4.0 ", "1.4.0")]
    public void Parse_Extracts_The_Semantic_Core(string raw, string expected)
    {
        FleetVersion.Parse(raw)!.ToString().Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("dev")]
    [InlineData(null)]
    public void Parse_Returns_Null_For_Unparseable(string? raw)
    {
        FleetVersion.Parse(raw).Should().BeNull();
    }

    [Fact]
    public void IsObsolete_True_When_Instance_Behind_Latest()
    {
        FleetVersion.IsObsolete("1.3.9", "1.4.0").Should().BeTrue();
        FleetVersion.IsObsolete("1.4.0+abc", "1.5.0+def").Should().BeTrue();
    }

    [Fact]
    public void IsObsolete_False_When_Current_Or_Newer()
    {
        FleetVersion.IsObsolete("1.4.0", "1.4.0").Should().BeFalse();
        FleetVersion.IsObsolete("1.5.0", "1.4.0").Should().BeFalse();
    }

    [Fact]
    public void IsObsolete_False_When_Either_Version_Is_Unparseable()
    {
        FleetVersion.IsObsolete("dev", "1.4.0").Should().BeFalse();
        FleetVersion.IsObsolete("1.3.0", string.Empty).Should().BeFalse();
    }
}
