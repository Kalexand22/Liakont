namespace Stratum.Common.UI.Tests.Unit;

using FluentAssertions;
using Stratum.Common.UI.Services;
using Xunit;

public sealed class FormErrorsTests
{
    private readonly FormErrors _sut = new();

    [Fact]
    public void NewInstanceShouldHaveNoErrors()
    {
        _sut.HasErrors.Should().BeFalse();
        _sut.GetError("any").Should().BeNull();
    }

    [Fact]
    public void SetFromExceptionShouldExtractFieldErrorsFromData()
    {
        var ex = new InvalidOperationException("Validation failed");
        ex.Data["Name"] = "Name is required";
        ex.Data["Email"] = "Invalid email";

        _sut.SetFromException(ex);

        _sut.HasErrors.Should().BeTrue();
        _sut.GetError("Name").Should().Be("Name is required");
        _sut.GetError("Email").Should().Be("Invalid email");
    }

    [Fact]
    public void SetFromExceptionShouldStoreMessageUnderEmptyKeyWhenDataIsEmpty()
    {
        var ex = new InvalidOperationException("Something went wrong");

        _sut.SetFromException(ex);

        _sut.HasErrors.Should().BeTrue();
        _sut.GetError(string.Empty).Should().Be("Something went wrong");
    }

    [Fact]
    public void SetFromExceptionShouldClearPreviousErrors()
    {
        var ex1 = new InvalidOperationException("First");
        ex1.Data["Field1"] = "Error1";
        _sut.SetFromException(ex1);

        var ex2 = new InvalidOperationException("Second");
        ex2.Data["Field2"] = "Error2";
        _sut.SetFromException(ex2);

        _sut.GetError("Field1").Should().BeNull();
        _sut.GetError("Field2").Should().Be("Error2");
    }

    [Fact]
    public void SetFromExceptionShouldThrowOnNullArgument()
    {
        var act = () => _sut.SetFromException(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SetFromExceptionShouldIgnoreNonStringDataEntries()
    {
        var ex = new InvalidOperationException("Mixed data");
        ex.Data["Name"] = "Name error";
        ex.Data[42] = "ignored (int key)";
        ex.Data["Count"] = 99;

        _sut.SetFromException(ex);

        _sut.GetError("Name").Should().Be("Name error");
        _sut.GetError("Count").Should().BeNull();
    }

    [Fact]
    public void SetFromProblemDetailsShouldExtractFirstMessage()
    {
        var errors = new Dictionary<string, string[]>
        {
            ["Name"] = ["Name is required", "Name too short"],
            ["Email"] = ["Invalid email"],
        };

        _sut.SetFromProblemDetails(errors);

        _sut.HasErrors.Should().BeTrue();
        _sut.GetError("Name").Should().Be("Name is required");
        _sut.GetError("Email").Should().Be("Invalid email");
    }

    [Fact]
    public void SetFromProblemDetailsShouldSkipEmptyArrays()
    {
        var errors = new Dictionary<string, string[]>
        {
            ["Name"] = [],
            ["Email"] = ["Invalid email"],
        };

        _sut.SetFromProblemDetails(errors);

        _sut.GetError("Name").Should().BeNull();
        _sut.GetError("Email").Should().Be("Invalid email");
    }

    [Fact]
    public void SetFromProblemDetailsShouldClearPreviousErrors()
    {
        _sut.SetFromProblemDetails(new Dictionary<string, string[]>
        {
            ["Field1"] = ["Error1"],
        });

        _sut.SetFromProblemDetails(new Dictionary<string, string[]>
        {
            ["Field2"] = ["Error2"],
        });

        _sut.GetError("Field1").Should().BeNull();
        _sut.GetError("Field2").Should().Be("Error2");
    }

    [Fact]
    public void SetFromProblemDetailsShouldSkipNullFirstMessage()
    {
        var errors = new Dictionary<string, string[]>
        {
            ["Name"] = [null!, "fallback"],
            ["Email"] = ["valid"],
        };

        _sut.SetFromProblemDetails(errors);

        _sut.GetError("Name").Should().BeNull();
        _sut.GetError("Email").Should().Be("valid");
    }

    [Fact]
    public void SetFromProblemDetailsShouldThrowOnNullArgument()
    {
        var act = () => _sut.SetFromProblemDetails(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ClearShouldRemoveAllErrors()
    {
        var ex = new InvalidOperationException("err");
        ex.Data["Field"] = "msg";
        _sut.SetFromException(ex);

        _sut.Clear();

        _sut.HasErrors.Should().BeFalse();
        _sut.GetError("Field").Should().BeNull();
    }

    [Fact]
    public void GetErrorShouldBeCaseInsensitive()
    {
        var ex = new InvalidOperationException("err");
        ex.Data["Name"] = "required";
        _sut.SetFromException(ex);

        _sut.GetError("name").Should().Be("required");
        _sut.GetError("NAME").Should().Be("required");
    }

    [Fact]
    public void SetErrorShouldAddSingleFieldError()
    {
        _sut.SetError("Name", "Name is required");

        _sut.HasErrors.Should().BeTrue();
        _sut.GetError("Name").Should().Be("Name is required");
    }

    [Fact]
    public void SetErrorShouldNotClearExistingErrors()
    {
        _sut.SetError("Name", "Name is required");
        _sut.SetError("Email", "Email is required");

        _sut.GetError("Name").Should().Be("Name is required");
        _sut.GetError("Email").Should().Be("Email is required");
    }

    [Fact]
    public void SetErrorShouldOverwriteSameField()
    {
        _sut.SetError("Name", "first error");
        _sut.SetError("Name", "second error");

        _sut.GetError("Name").Should().Be("second error");
        _sut.ErrorCount.Should().Be(1);
    }

    [Fact]
    public void SetErrorShouldThrowOnNullField()
    {
        var act = () => _sut.SetError(null!, "msg");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SetErrorShouldThrowOnNullMessage()
    {
        var act = () => _sut.SetError("field", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ErrorCountShouldReturnZeroWhenEmpty()
    {
        _sut.ErrorCount.Should().Be(0);
    }

    [Fact]
    public void ErrorCountShouldReturnCorrectCount()
    {
        _sut.SetError("A", "err");
        _sut.SetError("B", "err");
        _sut.SetError("C", "err");

        _sut.ErrorCount.Should().Be(3);
    }

    [Fact]
    public void AllErrorsShouldReturnAllFieldErrorPairs()
    {
        _sut.SetError("Name", "required");
        _sut.SetError("Email", "invalid");

        _sut.AllErrors.Should().HaveCount(2);
        _sut.AllErrors["Name"].Should().Be("required");
        _sut.AllErrors["Email"].Should().Be("invalid");
    }

    [Fact]
    public void AllErrorsShouldReturnEmptyWhenNoErrors()
    {
        _sut.AllErrors.Should().BeEmpty();
    }
}
