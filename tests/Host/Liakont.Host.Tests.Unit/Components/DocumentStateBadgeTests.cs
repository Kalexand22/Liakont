namespace Liakont.Host.Tests.Unit.Components;

using Bunit;
using FluentAssertions;
using Liakont.Host.Components;
using Xunit;

public sealed class DocumentStateBadgeTests : BunitContext
{
    public DocumentStateBadgeTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Should_Render_French_Label_For_A_Known_State()
    {
        var cut = Render<DocumentStateBadge>(p => p.Add(b => b.State, "Issued"));

        cut.Find("[data-testid='doc-state-Issued']").TextContent.Should().Contain("Émis");
    }

    [Fact]
    public void Should_Use_Default_TestId_From_State()
    {
        var cut = Render<DocumentStateBadge>(p => p.Add(b => b.State, "Blocked"));

        cut.FindAll("[data-testid='doc-state-Blocked']").Should().ContainSingle();
    }

    [Fact]
    public void Should_Render_Fallback_For_Unknown_State()
    {
        var cut = Render<DocumentStateBadge>(p => p.Add(b => b.State, "Mystery"));

        cut.Find("[data-testid='doc-state-Mystery']").TextContent.Should().Contain("Mystery");
    }
}
