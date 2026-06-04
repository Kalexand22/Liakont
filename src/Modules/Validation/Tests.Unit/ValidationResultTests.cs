namespace Liakont.Modules.Validation.Tests.Unit;

using FluentAssertions;
using Liakont.Modules.Validation.Contracts;
using Xunit;

public sealed class ValidationResultTests
{
    [Fact]
    public void Result_with_only_warnings_is_valid()
    {
        var result = new ValidationResult(new[] { ValidationIssue.Warning("W", "Alerte.") });

        result.IsValid.Should().BeTrue();
        result.HasBlockingIssue.Should().BeFalse();
    }

    [Fact]
    public void Result_with_a_blocking_issue_is_invalid()
    {
        var result = new ValidationResult(new[]
        {
            ValidationIssue.Warning("W", "Alerte."),
            ValidationIssue.Blocking("B", "Blocage."),
        });

        result.IsValid.Should().BeFalse();
        result.HasBlockingIssue.Should().BeTrue();
    }

    [Fact]
    public void Empty_result_is_valid()
    {
        var result = new ValidationResult(Array.Empty<ValidationIssue>());

        result.IsValid.Should().BeTrue();
        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public void Issue_requires_code_and_message()
    {
        var noCode = () => ValidationIssue.Blocking("  ", "msg");
        var noMessage = () => ValidationIssue.Blocking("CODE", "  ");

        noCode.Should().Throw<ArgumentException>();
        noMessage.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Blocking_factory_sets_severity_and_fields()
    {
        var issue = ValidationIssue.Blocking("DOC_TOTAL_MISMATCH", "Message opérateur.", "detail technique", "BT-112");

        issue.Severity.Should().Be(ValidationSeverity.Blocking);
        issue.Code.Should().Be("DOC_TOTAL_MISMATCH");
        issue.MessageOperateur.Should().Be("Message opérateur.");
        issue.DetailTechnique.Should().Be("detail technique");
        issue.FieldRef.Should().Be("BT-112");
    }

    [Fact]
    public void Warning_factory_sets_warning_severity()
    {
        var issue = ValidationIssue.Warning("SOURCE_TOTAL_MISMATCH", "Écart de totaux.");

        issue.Severity.Should().Be(ValidationSeverity.Warning);
        issue.DetailTechnique.Should().BeNull();
        issue.FieldRef.Should().BeNull();
    }
}
