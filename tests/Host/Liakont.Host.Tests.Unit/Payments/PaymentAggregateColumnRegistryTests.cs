namespace Liakont.Host.Tests.Unit.Payments;

using System.Linq;
using FluentAssertions;
using Liakont.Host.Payments;
using Stratum.Common.UI.Models;
using Xunit;

public sealed class PaymentAggregateColumnRegistryTests
{
    [Fact]
    public void Should_Declare_The_F10_Columns_With_Their_Keys()
    {
        var registry = new PaymentAggregateColumnRegistry();

        var keys = registry.GetAvailableColumns().Select(c => c.Key).ToList();

        // Les clés correspondent aux propriétés de PaymentAggregateRow (lecture/tri par la grille).
        keys.Should().Contain(["AggregateDate", "VatRate", "TaxableBase", "VatAmount", "Status", "Reason"]);
    }

    [Fact]
    public void Should_Type_Amounts_As_Money_The_Rate_As_Number_And_The_Day_As_Date()
    {
        var registry = new PaymentAggregateColumnRegistry();

        registry.GetColumn("AggregateDate")!.DataType.Should().Be(ColumnDataType.Date);
        registry.GetColumn("VatRate")!.DataType.Should().Be(ColumnDataType.Number);
        registry.GetColumn("TaxableBase")!.DataType.Should().Be(ColumnDataType.Money);
        registry.GetColumn("VatAmount")!.DataType.Should().Be(ColumnDataType.Money);
    }

    [Fact]
    public void Should_Expose_Status_As_Text_Without_Raw_Allowed_Values()
    {
        var registry = new PaymentAggregateColumnRegistry();

        var status = registry.GetColumn("Status")!;

        // Texte (pas Enum) : le filtre avancé n'exposerait sinon les clés brutes anglaises ;
        // le libellé français passe par le badge d'état du ColumnTemplate de la page.
        status.DataType.Should().Be(ColumnDataType.Text);
        status.AllowedValues.Should().BeNull();
    }

    [Fact]
    public void Should_Use_French_Titles()
    {
        var registry = new PaymentAggregateColumnRegistry();

        registry.GetColumn("AggregateDate")!.Title.Should().Be("Jour");
        registry.GetColumn("VatRate")!.Title.Should().Be("Taux");
        registry.GetColumn("TaxableBase")!.Title.Should().Be("Base HT");
        registry.GetColumn("VatAmount")!.Title.Should().Be("TVA");
        registry.GetColumn("Status")!.Title.Should().Be("État");
        registry.GetColumn("Reason")!.Title.Should().Be("Motif");
    }
}
