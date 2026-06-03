namespace Stratum.Modules.Notification.Tests.Unit.Domain;

using FluentAssertions;
using Stratum.Modules.Notification.Domain.Entities;
using Stratum.Modules.Notification.Domain.ValueObjects;
using Xunit;

public class EmailTemplateCategoryTests
{
    [Fact]
    public void Create_Should_Set_Category_And_EntityType()
    {
        var template = EmailTemplate.Create(
            "test-code",
            "Subject {{NAME}}",
            "Body {{NAME}}",
            "fr",
            null,
            TemplateCategory.Routing,
            "reservation");

        template.Category.Should().Be(TemplateCategory.Routing);
        template.EntityType.Should().Be("reservation");
        template.TemplateLinks.Should().BeEmpty();
    }

    [Fact]
    public void Create_Should_Accept_TemplateLinks()
    {
        var links = new[]
        {
            TemplateLink.Create("Dossier", "{{DOSSIER_URL}}"),
            TemplateLink.Create("Carte SIG", "{{SIG_URL}}"),
        };

        var template = EmailTemplate.Create(
            "routing-tmpl",
            "Subject",
            "Body",
            "fr",
            null,
            TemplateCategory.Routing,
            "reservation",
            links);

        template.TemplateLinks.Should().HaveCount(2);
        template.TemplateLinks[0].Label.Should().Be("Dossier");
        template.TemplateLinks[1].UrlTemplate.Should().Be("{{SIG_URL}}");
    }

    [Fact]
    public void Create_Default_Should_Be_Transactional()
    {
        var template = EmailTemplate.Create("code", "Subject", "Body", "en", null);

        template.Category.Should().Be(TemplateCategory.Transactional);
        template.EntityType.Should().BeNull();
    }

    [Fact]
    public void UpdateCategory_Should_Update_Fields()
    {
        var template = EmailTemplate.Create("code", "Subject", "Body", "en", null);

        var links = new[] { TemplateLink.Create("Link", "{{URL}}") };
        template.UpdateCategory(TemplateCategory.Escalation, "workflow", links);

        template.Category.Should().Be(TemplateCategory.Escalation);
        template.EntityType.Should().Be("workflow");
        template.TemplateLinks.Should().HaveCount(1);
        template.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public void Reconstitute_Should_Preserve_New_Fields()
    {
        var links = new[] { TemplateLink.Reconstitute("Lien", "{{URL}}") };

        var template = EmailTemplate.Reconstitute(
            Guid.NewGuid(),
            "code",
            "Subject",
            "Body",
            "fr",
            null,
            DateTimeOffset.UtcNow,
            null,
            TemplateCategory.Routing,
            "reservation",
            links);

        template.Category.Should().Be(TemplateCategory.Routing);
        template.EntityType.Should().Be("reservation");
        template.TemplateLinks.Should().HaveCount(1);
    }
}
