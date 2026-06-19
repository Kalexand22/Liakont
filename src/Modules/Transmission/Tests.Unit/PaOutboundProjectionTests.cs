namespace Liakont.Modules.Transmission.Tests.Unit;

using FluentAssertions;
using Liakont.Modules.Transmission.Contracts;
using Xunit;

/// <summary>
/// Projection sortante générique MND07 (F15 §1.2) : l'autofacturation sous mandat projette le type BT-3
/// « 389 » et le BT-1 FISCAL ALLOUÉ par mandant (MND05/ADR-0025), distinct du numéro source. Aucune règle
/// fiscale inventée — le code 389 est sourcé (UNTDID 1001, socle DGFiP V3.2) ; un 389 sans BT-1 fiscal
/// alloué n'est jamais projeté (« bloquer plutôt qu'émettre faux », CLAUDE.md n°2/3).
/// </summary>
public sealed class PaOutboundProjectionTests
{
    [Fact]
    public void DocumentTypeCodes_AreTheSourcedUntdid1001Values()
    {
        PaDocumentTypeCode.CommercialInvoice.Should().Be("380");
        PaDocumentTypeCode.SelfBilledInvoice.Should().Be("389");
    }

    [Fact]
    public void ForSelfBilled_Projects389AndTheAllocatedFiscalNumber()
    {
        var projection = PaOutboundProjection.ForSelfBilled("ARM-A-42");

        projection.DocumentTypeCode.Should().Be(PaDocumentTypeCode.SelfBilledInvoice);
        projection.FiscalNumber.Should().Be("ARM-A-42", "le BT-1 projeté est le numéro fiscal alloué (MND05)");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ForSelfBilled_WithoutAllocatedNumber_Throws_NeverProjectsA389WithoutFiscalBt1(string? allocated)
    {
        var act = () => PaOutboundProjection.ForSelfBilled(allocated!);

        act.Should().Throw<ArgumentException>("un 389 sans BT-1 fiscal alloué ne doit jamais atteindre la PA");
    }
}
