namespace Stratum.Common.Abstractions.Tests.Unit.Validation;

using FluentAssertions;
using Stratum.Common.Abstractions.Validation;
using Xunit;

public sealed class ValidationResultTests
{
    [Fact]
    public void Valid_NoFindings_ShouldReturnValidEmptyResult()
    {
        var result = ValidationResult.Valid();

        result.IsValid.Should().BeTrue();
        result.Findings.Should().BeEmpty();
    }

    [Fact]
    public void Valid_WithWarnings_ShouldReturnValidWithFindings()
    {
        var findings = new List<ValidationFinding>
        {
            new() { Severity = ValidationSeverity.Warning, Message = "Low stock" },
            new() { Severity = ValidationSeverity.Info, Message = "Note" },
        };

        var result = ValidationResult.Valid(findings);

        result.IsValid.Should().BeTrue();
        result.Findings.Should().HaveCount(2);
    }

    [Fact]
    public void Valid_WithErrorFindings_ShouldThrow()
    {
        var findings = new List<ValidationFinding>
        {
            new() { Severity = ValidationSeverity.Error, Message = "Bad" },
        };

        var act = () => ValidationResult.Valid(findings);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Invalid_WithFindings_ShouldReturnInvalidResult()
    {
        var findings = new List<ValidationFinding>
        {
            new() { Severity = ValidationSeverity.Error, Field = "Name", Message = "Required", Code = "INV-001" },
        };

        var result = ValidationResult.Invalid(findings);

        result.IsValid.Should().BeFalse();
        result.Findings.Should().HaveCount(1);
        result.Findings[0].Code.Should().Be("INV-001");
    }

    [Fact]
    public void Invalid_EmptyFindings_ShouldThrow()
    {
        var act = () => ValidationResult.Invalid(new List<ValidationFinding>());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Invalid_SingleMessage_ShouldCreateErrorFinding()
    {
        var result = ValidationResult.Invalid("Name is required", field: "Name", code: "INV-002");

        result.IsValid.Should().BeFalse();
        result.Findings.Should().HaveCount(1);
        result.Findings[0].Severity.Should().Be(ValidationSeverity.Error);
        result.Findings[0].Field.Should().Be("Name");
        result.Findings[0].Message.Should().Be("Name is required");
        result.Findings[0].Code.Should().Be("INV-002");
    }

    [Fact]
    public void Merge_MultipleResults_ShouldAggregateFindings()
    {
        var r1 = ValidationResult.Valid(
        [
            new ValidationFinding { Severity = ValidationSeverity.Warning, Message = "W1" },
        ]);
        var r2 = ValidationResult.Invalid("Error1");

        var merged = ValidationResult.Merge([r1, r2]);

        merged.IsValid.Should().BeFalse();
        merged.Findings.Should().HaveCount(2);
    }

    [Fact]
    public void Merge_AllValid_ShouldReturnValid()
    {
        var r1 = ValidationResult.Valid();
        var r2 = ValidationResult.Valid();

        var merged = ValidationResult.Merge([r1, r2]);

        merged.IsValid.Should().BeTrue();
        merged.Findings.Should().BeEmpty();
    }

    [Fact]
    public void Merge_EmptySequence_ShouldReturnValid()
    {
        var merged = ValidationResult.Merge([]);

        merged.IsValid.Should().BeTrue();
        merged.Findings.Should().BeEmpty();
    }
}
