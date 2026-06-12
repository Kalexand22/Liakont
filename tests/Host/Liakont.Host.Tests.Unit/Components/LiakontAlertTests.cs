namespace Liakont.Host.Tests.Unit.Components;

using Bunit;
using FluentAssertions;
using Liakont.Host.Components;
using Stratum.Common.UI.Models;
using Xunit;

public sealed class LiakontAlertTests : BunitContext
{
    [Fact]
    public void Should_Render_Content_With_TestId_And_Alert_Role_By_Default()
    {
        var cut = Render<LiakontAlert>(p => p
            .Add(a => a.TestId, "test-alert")
            .AddChildContent("Message opérateur"));

        var alert = cut.Find("[data-testid='test-alert']");
        alert.TextContent.Should().Contain("Message opérateur");
        alert.GetAttribute("role").Should().Be("alert");
    }

    [Fact]
    public void Should_Default_To_Error_Severity()
    {
        var cut = Render<LiakontAlert>(p => p.AddChildContent("Échec"));

        cut.Find(".liakont-alert").ClassList.Should().Contain("liakont-alert--error");
    }

    [Theory]
    [InlineData(Severity.Error, "liakont-alert--error")]
    [InlineData(Severity.Warning, "liakont-alert--warning")]
    [InlineData(Severity.Info, "liakont-alert--info")]
    [InlineData(Severity.Success, "liakont-alert--success")]
    [InlineData(Severity.Neutral, "liakont-alert--neutral")]
    public void Should_Map_Severity_To_Modifier_Class(Severity severity, string expectedClass)
    {
        var cut = Render<LiakontAlert>(p => p
            .Add(a => a.Severity, severity)
            .AddChildContent("Message"));

        cut.Find(".liakont-alert").ClassList.Should().Contain(expectedClass);
    }

    [Fact]
    public void Should_Allow_Status_Role_For_Non_Urgent_Messages()
    {
        var cut = Render<LiakontAlert>(p => p
            .Add(a => a.Role, "status")
            .AddChildContent("Information"));

        cut.Find(".liakont-alert").GetAttribute("role").Should().Be("status");
    }
}
