namespace Stratum.Common.UI.Tests.Unit;

using FluentAssertions;
using Stratum.Common.UI.Components;
using Xunit;

public sealed class LinearCircularProgressTests
{
    [Theory]
    [InlineData(-10, 0)]
    [InlineData(0, 0)]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    [InlineData(110, 100)]
    public void LinearClampValueShouldClampTo0And100(double input, double expected) =>
        LinearProgress.ClampValue(input).Should().Be(expected);

    [Theory]
    [InlineData(false, false, "linear-progress__bar")]
    [InlineData(true, false, "linear-progress__bar linear-progress__bar--striped")]
    [InlineData(true, true, "linear-progress__bar linear-progress__bar--striped linear-progress__bar--animated")]
    [InlineData(false, true, "linear-progress__bar")]
    public void LinearBuildBarClassShouldBuildCorrectly(bool striped, bool animated, string expected) =>
        LinearProgress.BuildBarClass(striped, animated).Should().Be(expected);

    [Theory]
    [InlineData(ProgressColor.Default, false, null, "linear-progress linear-progress--default")]
    [InlineData(ProgressColor.Info, false, null, "linear-progress linear-progress--info")]
    [InlineData(ProgressColor.Success, true, null, "linear-progress linear-progress--success linear-progress--indeterminate")]
    [InlineData(ProgressColor.Warning, false, "my-class", "linear-progress linear-progress--warning my-class")]
    [InlineData(ProgressColor.Danger, true, "extra", "linear-progress linear-progress--danger linear-progress--indeterminate extra")]
    public void LinearBuildRootClassShouldBuildCorrectly(
        ProgressColor color, bool indeterminate, string? cssClass, string expected) =>
        LinearProgress.BuildRootClass(color, indeterminate, cssClass).Should().Be(expected);

    [Fact]
    public void LinearFormattedPercentShouldUseInvariantCulture()
    {
        var result = LinearProgress.FormattedPercent(75.5);
        result.Should().Be("75.50%");
    }

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(0, 0)]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    [InlineData(110, 100)]
    public void CircularClampValueShouldClampTo0And100(double input, double expected) =>
        CircularProgress.ClampValue(input).Should().Be(expected);

    [Fact]
    public void CircularComputeRadiusShouldReturnHalfSizeMinusHalfThickness()
    {
        CircularProgress.ComputeRadius(40, 4).Should().Be(18.0);
    }

    [Fact]
    public void CircularComputeCircumferenceShouldReturn2PiR()
    {
        var expected = 2 * Math.PI * 18.0;
        CircularProgress.ComputeCircumference(40, 4).Should().BeApproximately(expected, 0.001);
    }

    [Fact]
    public void CircularComputeDashOffsetShouldReturnCorrectOffset()
    {
        var circumference = 2 * Math.PI * 18.0;
        var expected = circumference * 0.25;
        CircularProgress.ComputeDashOffset(75, circumference).Should().BeApproximately(expected, 0.001);
    }

    [Theory]
    [InlineData(ProgressColor.Default, false, null, "circular-progress circular-progress--default")]
    [InlineData(ProgressColor.Info, false, null, "circular-progress circular-progress--info")]
    [InlineData(ProgressColor.Success, true, null, "circular-progress circular-progress--success circular-progress--indeterminate")]
    [InlineData(ProgressColor.Warning, false, "extra", "circular-progress circular-progress--warning extra")]
    [InlineData(ProgressColor.Danger, true, "x", "circular-progress circular-progress--danger circular-progress--indeterminate x")]
    public void CircularBuildRootClassShouldBuildCorrectly(
        ProgressColor color, bool indeterminate, string? cssClass, string expected) =>
        CircularProgress.BuildRootClass(color, indeterminate, cssClass).Should().Be(expected);
}
