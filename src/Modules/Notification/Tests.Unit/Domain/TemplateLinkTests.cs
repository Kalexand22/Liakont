namespace Stratum.Modules.Notification.Tests.Unit.Domain;

using FluentAssertions;
using Stratum.Modules.Notification.Domain.ValueObjects;
using Xunit;

public class TemplateLinkTests
{
    [Fact]
    public void Create_Should_Set_Label_And_UrlTemplate()
    {
        var link = TemplateLink.Create("Dossier", "{{DOSSIER_URL}}");

        link.Label.Should().Be("Dossier");
        link.UrlTemplate.Should().Be("{{DOSSIER_URL}}");
    }

    [Fact]
    public void Create_Should_Trim_Values()
    {
        var link = TemplateLink.Create("  Dossier  ", "  {{URL}}  ");

        link.Label.Should().Be("Dossier");
        link.UrlTemplate.Should().Be("{{URL}}");
    }

    [Fact]
    public void Create_Should_Throw_When_Label_Empty()
    {
        var act = () => TemplateLink.Create(string.Empty, "{{URL}}");
        act.Should().Throw<ArgumentException>().WithMessage("*INV-NOTIF-020*");
    }

    [Fact]
    public void Create_Should_Throw_When_UrlTemplate_Empty()
    {
        var act = () => TemplateLink.Create("Label", string.Empty);
        act.Should().Throw<ArgumentException>().WithMessage("*INV-NOTIF-021*");
    }

    [Fact]
    public void Reconstitute_Should_Preserve_Values()
    {
        var link = TemplateLink.Reconstitute("Label", "{{URL}}");

        link.Label.Should().Be("Label");
        link.UrlTemplate.Should().Be("{{URL}}");
    }
}
