namespace Stratum.Common.Abstractions.Tests.Unit.FieldChange;

using FluentAssertions;
using Stratum.Common.Abstractions.FieldChange;
using Xunit;

public sealed class OnChangeAttributeTests
{
    [Fact]
    public void Constructor_ValidFieldName_ShouldSetProperty()
    {
        var attr = new OnChangeAttribute("CustomerName");

        attr.FieldName.Should().Be("CustomerName");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_NullOrWhitespace_ShouldThrow(string? fieldName)
    {
        var act = () => new OnChangeAttribute(fieldName!);

        act.Should().Throw<ArgumentException>();
    }
}
