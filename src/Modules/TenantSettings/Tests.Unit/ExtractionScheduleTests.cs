namespace Liakont.Modules.TenantSettings.Tests.Unit;

using FluentAssertions;
using Liakont.Modules.TenantSettings.Domain.Entities;
using Xunit;

public sealed class ExtractionScheduleTests
{
    [Fact]
    public void Create_With_Valid_Hours_Succeeds()
    {
        var schedule = ExtractionSchedule.Create(Guid.NewGuid(), ["03:00", "15:30"], catchUpOnStart: true);

        schedule.Hours.Should().Equal("03:00", "15:30");
        schedule.CatchUpOnStart.Should().BeTrue();
    }

    [Theory]
    [InlineData("3:00")]
    [InlineData("25:00")]
    [InlineData("midnight")]
    public void Create_With_Invalid_Hour_Throws(string badHour)
    {
        var act = () => ExtractionSchedule.Create(Guid.NewGuid(), [badHour], catchUpOnStart: false);

        act.Should().Throw<ArgumentException>().WithMessage("*INV-TENANTSETTINGS-002*");
    }
}
