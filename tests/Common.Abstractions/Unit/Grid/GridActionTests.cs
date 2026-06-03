namespace Stratum.Common.Abstractions.Tests.Unit.Grid;

using FluentAssertions;
using Stratum.Common.Abstractions.Grid;
using Xunit;

public sealed class GridActionTests
{
    [Fact]
    public void DefaultsShouldBeReasonable()
    {
        var action = new GridAction("test", "Test");

        action.Id.Should().Be("test");
        action.Label.Should().Be("Test");
        action.Icon.Should().BeNull();
        action.Callback.Should().BeNull();
        action.IsEnabled.Should().BeNull();
        action.IsPrimary.Should().BeFalse();
        action.RequiresSelection.Should().BeFalse();
        action.RequiresConfirmation.Should().BeFalse();
    }

    [Fact]
    public void EvaluateEnabledShouldReturnTrueByDefault()
    {
        var action = new GridAction("test", "Test");

        action.EvaluateEnabled(selectedCount: 0).Should().BeTrue();
    }

    [Fact]
    public void EvaluateEnabledShouldRespectIsEnabled()
    {
        var action = new GridAction("test", "Test", IsEnabled: () => false);

        action.EvaluateEnabled(selectedCount: 5).Should().BeFalse();
    }

    [Fact]
    public void EvaluateEnabledShouldDisableWhenRequiresSelectionAndNoneSelected()
    {
        var action = new GridAction("test", "Test", RequiresSelection: true);

        action.EvaluateEnabled(selectedCount: 0).Should().BeFalse();
    }

    [Fact]
    public void EvaluateEnabledShouldEnableWhenRequiresSelectionAndSomeSelected()
    {
        var action = new GridAction("test", "Test", RequiresSelection: true);

        action.EvaluateEnabled(selectedCount: 3).Should().BeTrue();
    }

    [Fact]
    public void EvaluateEnabledShouldCombineRequiresSelectionWithIsEnabled()
    {
        var action = new GridAction(
            "test",
            "Test",
            RequiresSelection: true,
            IsEnabled: () => false);

        // Even with selection, IsEnabled returns false
        action.EvaluateEnabled(selectedCount: 5).Should().BeFalse();
    }

    [Fact]
    public void EvaluateEnabledShouldShortCircuitOnRequiresSelection()
    {
        var isEnabledCalled = false;
        var action = new GridAction(
            "test",
            "Test",
            RequiresSelection: true,
            IsEnabled: () =>
            {
                isEnabledCalled = true;
                return true;
            });

        action.EvaluateEnabled(selectedCount: 0);

        // IsEnabled should not be called when RequiresSelection blocks
        isEnabledCalled.Should().BeFalse();
    }

    [Fact]
    public void AllPropertiesShouldBeStored()
    {
        Func<Task> callback = () => Task.CompletedTask;
        Func<bool> isEnabled = () => true;

        var action = new GridAction(
            Id: "delete",
            Label: "Supprimer",
            Icon: "bi-trash",
            Callback: callback,
            IsEnabled: isEnabled,
            IsPrimary: true,
            RequiresSelection: true,
            RequiresConfirmation: true);

        action.Id.Should().Be("delete");
        action.Label.Should().Be("Supprimer");
        action.Icon.Should().Be("bi-trash");
        action.Callback.Should().BeSameAs(callback);
        action.IsEnabled.Should().BeSameAs(isEnabled);
        action.IsPrimary.Should().BeTrue();
        action.RequiresSelection.Should().BeTrue();
        action.RequiresConfirmation.Should().BeTrue();
    }

    [Fact]
    public void RecordEqualityShouldBeValueBased()
    {
        var a = new GridAction("create", "Nouveau", Icon: "bi-plus");
        var b = new GridAction("create", "Nouveau", Icon: "bi-plus");

        a.Should().Be(b);
    }
}
