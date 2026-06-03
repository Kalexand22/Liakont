namespace Stratum.Common.Abstractions.Tests.Unit.Grid;

using FluentAssertions;
using Stratum.Common.Abstractions.Grid;
using Xunit;

public sealed class UserGridPreferenceTests
{
    [Fact]
    public void RecordShouldPreserveAllProperties()
    {
        var id = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var keys = new List<string> { "Name", "Email", "Customer.Name" }.AsReadOnly();
        var created = DateTimeOffset.UtcNow;

        var pref = new UserGridPreference(id, userId, "Sales.InvoiceList.Main", keys, created, null);

        pref.Id.Should().Be(id);
        pref.UserId.Should().Be(userId);
        pref.GridKey.Should().Be("Sales.InvoiceList.Main");
        pref.ColumnKeys.Should().BeEquivalentTo(keys, o => o.WithStrictOrdering());
        pref.CreatedAt.Should().Be(created);
        pref.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public void RecordShouldSupportWithExpression()
    {
        var pref = CreatePreference();
        var updated = DateTimeOffset.UtcNow;

        var modified = pref with { UpdatedAt = updated };

        modified.UpdatedAt.Should().Be(updated);
        modified.GridKey.Should().Be(pref.GridKey);
    }

    [Fact]
    public void RecordShouldSupportValueEquality()
    {
        var id = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var keys = new List<string> { "A", "B" }.AsReadOnly();
        var ts = DateTimeOffset.UtcNow;

        var a = new UserGridPreference(id, userId, "key", keys, ts, null);
        var b = new UserGridPreference(id, userId, "key", keys, ts, null);

        a.Should().Be(b);
    }

    [Fact]
    public void ColumnKeysShouldPreserveOrder()
    {
        var keys = new List<string> { "Z", "A", "M" }.AsReadOnly();
        var pref = new UserGridPreference(Guid.NewGuid(), Guid.NewGuid(), "g", keys, DateTimeOffset.UtcNow, null);

        pref.ColumnKeys.Should().ContainInOrder("Z", "A", "M");
    }

    [Fact]
    public void GridKeyShouldFollowModulePageGridConvention()
    {
        var pref = CreatePreference(gridKey: "Party.PartyList.Main");

        pref.GridKey.Should().Contain(".");
        pref.GridKey.Split('.').Should().HaveCount(3);
    }

    private static UserGridPreference CreatePreference(
        string gridKey = "Sales.InvoiceList.Main")
    {
        return new UserGridPreference(
            Guid.NewGuid(),
            Guid.NewGuid(),
            gridKey,
            new List<string> { "Name", "Amount" }.AsReadOnly(),
            DateTimeOffset.UtcNow,
            null);
    }
}
