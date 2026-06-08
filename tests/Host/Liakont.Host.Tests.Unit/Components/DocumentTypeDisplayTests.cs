namespace Liakont.Host.Tests.Unit.Components;

using FluentAssertions;
using Liakont.Host.Components;
using Xunit;

public sealed class DocumentTypeDisplayTests
{
    [Theory]
    [InlineData("invoice", "Facture")]
    [InlineData("Invoice", "Facture")]
    [InlineData("INVOICE", "Facture")]
    [InlineData("credit_note", "Avoir")]
    [InlineData("Credit_Note", "Avoir")]
    [InlineData("credit-note", "Avoir")]
    [InlineData("creditNote", "Avoir")]
    public void For_Should_Map_Known_Kinds_To_French_Regardless_Of_Case(string raw, string expected)
    {
        DocumentTypeDisplay.For(raw).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void For_Should_Return_Dash_For_Empty(string? raw)
    {
        DocumentTypeDisplay.For(raw).Should().Be("—");
    }

    [Fact]
    public void For_Should_Fall_Back_To_The_Raw_Value_For_Unknown_Types()
    {
        // Produit générique : un type source non reconnu n'est jamais masqué ni deviné.
        DocumentTypeDisplay.For("bordereau-vente").Should().Be("bordereau-vente");
    }
}
