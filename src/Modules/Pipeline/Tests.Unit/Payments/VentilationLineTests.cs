namespace Liakont.Modules.Pipeline.Tests.Unit.Payments;

using FluentAssertions;
using Liakont.Modules.Pipeline.Domain.Ventilation;
using Xunit;

/// <summary>
/// Garde-fou d'intégrité de stockage de la ventilation (ADR-0015, CLAUDE.md n°1/4) : une valeur dépassant
/// l'échéance de la colonne serait tronquée silencieusement (montant fiscal altéré) — rejetée AVANT persistance.
/// </summary>
public sealed class VentilationLineTests
{
    [Fact]
    public void Accepts_Null_Rate_And_Two_Decimal_Amounts()
    {
        var line = VentilationLine.Create(null, 100.00m, 20.00m);

        line.Rate.Should().BeNull();
        line.TaxableBase.Should().Be(100.00m);
        line.VatAmount.Should().Be(20.00m);
    }

    [Fact]
    public void Rejects_Amount_With_More_Than_Two_Decimals()
    {
        var act = () => VentilationLine.Create(20m, 100.001m, 20.00m);

        act.Should().Throw<System.ArgumentException>();
    }

    [Fact]
    public void Rejects_Rate_With_More_Than_Four_Decimals()
    {
        var act = () => VentilationLine.Create(20.00001m, 100.00m, 20.00m);

        act.Should().Throw<System.ArgumentException>();
    }
}
