namespace Liakont.Modules.Pipeline.Tests.Unit.Payments;

using FluentAssertions;
using Liakont.Modules.Pipeline.Domain.Ventilation;
using Xunit;

/// <summary>
/// La ligne de ventilation (ADR-0015) PRÉSERVE les valeurs telles que produites par le mapping validé
/// (INV-VENTILATION-001) — montants `decimal` (jamais float, CLAUDE.md n°1), précision conservée par la
/// sérialisation en chaîne jsonb du snapshot ; aucun arrondi ni recalcul ici.
/// </summary>
public sealed class VentilationLineTests
{
    [Fact]
    public void Preserves_Rate_Amounts_And_Category()
    {
        var line = VentilationLine.Create(20m, 100.00m, 20.00m, "S");

        line.Rate.Should().Be(20m);
        line.TaxableBase.Should().Be(100.00m);
        line.VatAmount.Should().Be(20.00m);
        line.Category.Should().Be("S");
    }

    [Fact]
    public void Accepts_Null_Rate_And_Null_Category()
    {
        // Taux non résolu au CHECK → null (l'agrégation suspendra), catégorie non posée → null.
        var line = VentilationLine.Create(null, 100.00m, 20.00m);

        line.Rate.Should().BeNull();
        line.Category.Should().BeNull();
    }

    [Fact]
    public void Preserves_Decimal_Precision_Without_Rounding()
    {
        // La précision est conservée telle quelle (le snapshot jsonb la stocke en chaîne) — aucun arrondi
        // silencieux, aucune troncature (INV-VENTILATION-001/002).
        var line = VentilationLine.Create(20m, 100.005m, 20.001m);

        line.TaxableBase.Should().Be(100.005m);
        line.VatAmount.Should().Be(20.001m);
    }
}
