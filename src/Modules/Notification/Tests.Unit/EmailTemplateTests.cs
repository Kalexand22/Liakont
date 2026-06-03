namespace Stratum.Modules.Notification.Tests.Unit;

using FluentAssertions;
using Stratum.Modules.Notification.Domain.Entities;
using Xunit;

public class EmailTemplateTests
{
    [Fact]
    public void Create_Should_Succeed_With_Valid_Parameters()
    {
        var template = EmailTemplate.Create("WELCOME", "Welcome {{name}}", "Hello {{name}}", "en", null);

        template.Id.Should().NotBeEmpty();
        template.Code.Should().Be("WELCOME");
        template.SubjectTemplate.Should().Be("Welcome {{name}}");
        template.BodyTemplate.Should().Be("Hello {{name}}");
        template.LanguageCode.Should().Be("en");
        template.CompanyId.Should().BeNull();
        template.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        template.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public void Create_Should_Trim_Code_And_Normalize_Language()
    {
        var template = EmailTemplate.Create("  WELCOME  ", "Subject", "Body", "FR", null);

        template.Code.Should().Be("WELCOME");
        template.LanguageCode.Should().Be("fr");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Should_Throw_When_Code_Empty(string? code)
    {
        var act = () => EmailTemplate.Create(code!, "Subject", "Body", "en", null);

        act.Should().Throw<ArgumentException>().WithMessage("*INV-NOTIF-002*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Should_Throw_When_SubjectTemplate_Empty(string? subject)
    {
        var act = () => EmailTemplate.Create("CODE", subject!, "Body", "en", null);

        act.Should().Throw<ArgumentException>().WithMessage("*INV-NOTIF-003*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Should_Throw_When_BodyTemplate_Empty(string? body)
    {
        var act = () => EmailTemplate.Create("CODE", "Subject", body!, "en", null);

        act.Should().Throw<ArgumentException>().WithMessage("*INV-NOTIF-004*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("e")]
    [InlineData("eng")]
    public void Create_Should_Throw_When_LanguageCode_Invalid(string languageCode)
    {
        var act = () => EmailTemplate.Create("CODE", "Subject", "Body", languageCode, null);

        act.Should().Throw<ArgumentException>().WithMessage("*INV-NOTIF-005*");
    }

    [Fact]
    public void Update_Should_Set_SubjectTemplate_BodyTemplate_And_UpdatedAt()
    {
        var template = EmailTemplate.Create("CODE", "Old Subject", "Old Body", "en", null);

        template.Update("New Subject", "New Body");

        template.SubjectTemplate.Should().Be("New Subject");
        template.BodyTemplate.Should().Be("New Body");
        template.UpdatedAt.Should().NotBeNull();
        template.UpdatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Update_Should_Throw_When_SubjectTemplate_Empty()
    {
        var template = EmailTemplate.Create("CODE", "Subject", "Body", "en", null);

        var act = () => template.Update(string.Empty, "New Body");

        act.Should().Throw<ArgumentException>().WithMessage("*INV-NOTIF-003*");
    }

    [Fact]
    public void Update_Should_Throw_When_BodyTemplate_Empty()
    {
        var template = EmailTemplate.Create("CODE", "Subject", "Body", "en", null);

        var act = () => template.Update("New Subject", string.Empty);

        act.Should().Throw<ArgumentException>().WithMessage("*INV-NOTIF-004*");
    }

    [Fact]
    public void Reconstitute_Should_Return_Entity_With_All_Fields()
    {
        var id = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow.AddDays(-1);
        var updatedAt = DateTimeOffset.UtcNow;

        var template = EmailTemplate.Reconstitute(
            id, "CODE", "Subject", "Body", "fr", companyId, createdAt, updatedAt);

        template.Id.Should().Be(id);
        template.Code.Should().Be("CODE");
        template.SubjectTemplate.Should().Be("Subject");
        template.BodyTemplate.Should().Be("Body");
        template.LanguageCode.Should().Be("fr");
        template.CompanyId.Should().Be(companyId);
        template.CreatedAt.Should().Be(createdAt);
        template.UpdatedAt.Should().Be(updatedAt);
    }
}
