namespace Stratum.Modules.Notification.Tests.Unit.Domain;

using FluentAssertions;
using Stratum.Modules.Notification.Domain.Entities;
using Xunit;

public class ServiceDefinitionTests
{
    [Fact]
    public void Create_sets_properties_correctly()
    {
        var service = ServiceDefinition.Create("voirie", "Service Voirie", "voirie@commune.fr", "Service technique voirie", null);

        service.Id.Should().NotBeEmpty();
        service.Code.Should().Be("voirie");
        service.Name.Should().Be("Service Voirie");
        service.Email.Should().Be("voirie@commune.fr");
        service.Description.Should().Be("Service technique voirie");
        service.IsActive.Should().BeTrue();
        service.CompanyId.Should().BeNull();
    }

    [Fact]
    public void Create_throws_on_empty_code()
    {
        var act = () => ServiceDefinition.Create(string.Empty, "Name", "email@test.fr", null, null);

        act.Should().Throw<ArgumentException>().WithMessage("*INV-NOTIF-011*");
    }

    [Fact]
    public void Create_throws_on_empty_name()
    {
        var act = () => ServiceDefinition.Create("code", string.Empty, "email@test.fr", null, null);

        act.Should().Throw<ArgumentException>().WithMessage("*name*");
    }

    [Fact]
    public void Create_throws_on_empty_email()
    {
        var act = () => ServiceDefinition.Create("code", "Name", string.Empty, null, null);

        act.Should().Throw<ArgumentException>().WithMessage("*email*");
    }

    [Fact]
    public void Update_modifies_fields()
    {
        var service = ServiceDefinition.Create("voirie", "Service Voirie", "voirie@commune.fr", null, null);

        service.Update("Service Voirie Modifie", "voirie-new@commune.fr", "Desc updated", false);

        service.Name.Should().Be("Service Voirie Modifie");
        service.Email.Should().Be("voirie-new@commune.fr");
        service.Description.Should().Be("Desc updated");
        service.IsActive.Should().BeFalse();
        service.UpdatedAt.Should().NotBeNull();
    }
}
