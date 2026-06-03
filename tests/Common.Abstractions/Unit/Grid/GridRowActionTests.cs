namespace Stratum.Common.Abstractions.Tests.Unit.Grid;

using FluentAssertions;
using Stratum.Common.Abstractions.Grid;
using Xunit;

public sealed class GridRowActionTests
{
    [Fact]
    public void DefaultsShouldBeReasonable()
    {
        var action = new GridRowAction<string>("test", "Test");

        action.Id.Should().Be("test");
        action.Label.Should().Be("Test");
        action.Icon.Should().BeNull();
        action.Callback.Should().BeNull();
        action.IsEnabled.Should().BeNull();
        action.RequiresConfirmation.Should().BeFalse();
        action.IsSeparatorBefore.Should().BeFalse();
    }

    [Fact]
    public void EvaluateEnabledShouldReturnTrueByDefault()
    {
        var action = new GridRowAction<string>("test", "Test");

        action.EvaluateEnabled("any item").Should().BeTrue();
    }

    [Fact]
    public void EvaluateEnabledShouldRespectIsEnabled()
    {
        var action = new GridRowAction<string>(
            "test",
            "Test",
            IsEnabled: item => item == "active");

        action.EvaluateEnabled("active").Should().BeTrue();
        action.EvaluateEnabled("inactive").Should().BeFalse();
    }

    [Fact]
    public void EvaluateEnabledShouldReceiveCorrectItem()
    {
        string? receivedItem = null;
        var action = new GridRowAction<string>(
            "test",
            "Test",
            IsEnabled: item =>
            {
                receivedItem = item;
                return true;
            });

        action.EvaluateEnabled("hello");

        receivedItem.Should().Be("hello");
    }

    [Fact]
    public void AllPropertiesShouldBeStored()
    {
        Func<string, Task> callback = _ => Task.CompletedTask;
        Func<string, bool> isEnabled = _ => true;

        var action = new GridRowAction<string>(
            Id: "delete",
            Label: "Supprimer",
            Icon: "bi-trash",
            Callback: callback,
            IsEnabled: isEnabled,
            RequiresConfirmation: true,
            IsSeparatorBefore: true);

        action.Id.Should().Be("delete");
        action.Label.Should().Be("Supprimer");
        action.Icon.Should().Be("bi-trash");
        action.Callback.Should().BeSameAs(callback);
        action.IsEnabled.Should().BeSameAs(isEnabled);
        action.RequiresConfirmation.Should().BeTrue();
        action.IsSeparatorBefore.Should().BeTrue();
    }

    [Fact]
    public void RecordEqualityShouldBeValueBased()
    {
        var a = new GridRowAction<string>("view", "Voir", Icon: "bi-eye");
        var b = new GridRowAction<string>("view", "Voir", Icon: "bi-eye");

        a.Should().Be(b);
    }

    [Fact]
    public async Task CallbackShouldReceiveItem()
    {
        string? calledWith = null;
        var action = new GridRowAction<string>(
            "test",
            "Test",
            Callback: item =>
            {
                calledWith = item;
                return Task.CompletedTask;
            });

        await action.Callback!("row-data");

        calledWith.Should().Be("row-data");
    }
}
