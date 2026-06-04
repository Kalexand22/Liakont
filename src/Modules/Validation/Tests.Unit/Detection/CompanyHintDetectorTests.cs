namespace Liakont.Modules.Validation.Tests.Unit.Detection;

using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Validation.Domain.Detection;
using Xunit;

public sealed class CompanyHintDetectorTests
{
    [Fact]
    public void Plain_individual_is_not_professional()
    {
        var result = CompanyHintDetector.Detect(new PivotPartyDto("Jean Dupont"));

        result.LooksProfessional.Should().BeFalse();
        result.HasCompanyHintField.Should().BeFalse();
        result.HasVatNumber.Should().BeFalse();
        result.HasLegalForm.Should().BeFalse();
        result.MatchedLegalForm.Should().BeNull();
    }

    [Fact]
    public void Raw_societe_hint_is_a_strong_indicator()
    {
        // Indice FORT : transcription brute du champ source « societe » (aucune heuristique côté agent).
        var result = CompanyHintDetector.Detect(new PivotPartyDto("Jean Dupont", isCompanyHint: true));

        result.HasCompanyHintField.Should().BeTrue();
        result.LooksProfessional.Should().BeTrue();
    }

    [Fact]
    public void Present_vat_number_is_a_strong_indicator()
    {
        var result = CompanyHintDetector.Detect(new PivotPartyDto("Jean Dupont", vatNumber: "FR40303265045"));

        result.HasVatNumber.Should().BeTrue();
        result.LooksProfessional.Should().BeTrue();
    }

    [Fact]
    public void Present_foreign_vat_number_counts_even_when_not_french()
    {
        // F07-F08 §A.4 : « présent », pas « valide FR » — un n° étranger signale tout autant un pro.
        var result = CompanyHintDetector.Detect(new PivotPartyDto("KÄUFER GMBH", vatNumber: "DE123456789"));

        result.HasVatNumber.Should().BeTrue();
        result.LooksProfessional.Should().BeTrue();
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("")]
    public void Blank_vat_number_is_not_an_indicator(string vatNumber)
    {
        var result = CompanyHintDetector.Detect(new PivotPartyDto("Jean Dupont", vatNumber: vatNumber));

        result.HasVatNumber.Should().BeFalse();
        result.LooksProfessional.Should().BeFalse();
    }

    [Theory]
    [InlineData("MARTIN SARL")]
    [InlineData("MARTIN SAS")]
    [InlineData("MARTIN SA")]
    [InlineData("MARTIN EURL")]
    [InlineData("DUPONT EI")]
    [InlineData("martin sarl")] // insensible à la casse
    public void Legal_form_in_name_is_a_medium_indicator(string name)
    {
        var result = CompanyHintDetector.Detect(new PivotPartyDto(name));

        result.HasLegalForm.Should().BeTrue();
        result.MatchedLegalForm.Should().NotBeNull();
        result.LooksProfessional.Should().BeTrue();
    }

    [Theory]
    [InlineData("BEIGNET")] // « EI » en sous-chaîne, pas un token
    [InlineData("SABATIER")] // « SA » en sous-chaîne, pas un token
    [InlineData("Galerie Saint-Martin")] // « SA » en début de « Saint », pas un token
    [InlineData("Jean Dupont")]
    public void Substring_of_a_legal_form_is_not_detected(string name)
    {
        var result = CompanyHintDetector.Detect(new PivotPartyDto(name));

        result.HasLegalForm.Should().BeFalse();
        result.MatchedLegalForm.Should().BeNull();
        result.LooksProfessional.Should().BeFalse();
    }

    [Fact]
    public void Null_buyer_is_rejected()
    {
        var act = () => CompanyHintDetector.Detect(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
