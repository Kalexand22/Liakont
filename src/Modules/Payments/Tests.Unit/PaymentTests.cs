namespace Liakont.Modules.Payments.Tests.Unit;

using System;
using FluentAssertions;
using Liakont.Modules.Payments.Domain.Entities;
using Xunit;

/// <summary>
/// Encaissement brut <see cref="Payment"/> (F09 / TRK04) : report fidèle du montant source (decimal,
/// CLAUDE.md n°1), garde-fou d'intégrité de stockage (montant à plus de 2 décimales rejeté), montant négatif
/// autorisé (remboursement — F09 §5.4), champs optionnels vides ramenés à <c>null</c>.
/// </summary>
public sealed class PaymentTests
{
    private static readonly DateTimeOffset T0 = new(2026, 5, 14, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly D0 = new(2026, 5, 14);

    [Fact]
    public void Create_Keeps_Source_Amount_And_Fields()
    {
        var payment = Payment.Create(Guid.NewGuid(), D0, 1162.80m, "CB", "F-2026-001", "ENC-1", T0);

        payment.PaymentDate.Should().Be(D0);
        payment.Amount.Should().Be(1162.80m);
        payment.Method.Should().Be("CB");
        payment.RelatedDocumentNumber.Should().Be("F-2026-001");
        payment.SourceReference.Should().Be("ENC-1");
        payment.ReceivedUtc.Should().Be(T0);
    }

    [Fact]
    public void Create_Keeps_Decimal_Amount_Exact_Without_Float_Drift()
    {
        // 0.1 + 0.2 piège les flottants ; en decimal le montant est exact (CLAUDE.md n°1).
        var payment = Payment.Create(Guid.NewGuid(), D0, 0.1m + 0.2m, null, null, null, T0);

        payment.Amount.Should().Be(0.3m);
    }

    [Fact]
    public void Create_Allows_Negative_Amount_For_Refund()
    {
        var payment = Payment.Create(Guid.NewGuid(), D0, -50.00m, null, null, null, T0);

        payment.Amount.Should().Be(-50.00m, "un trop-perçu / remboursement est un montant négatif (F09 §5.4).");
    }

    [Theory]
    [InlineData("12.345")]
    [InlineData("0.001")]
    public void Create_Rejects_Amount_With_More_Than_2_Decimals(string raw)
    {
        var value = decimal.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);

        var act = () => Payment.Create(Guid.NewGuid(), D0, value, null, null, null, T0);

        act.Should().Throw<ArgumentException>().WithParameterName("amount");
    }

    [Theory]
    [InlineData("1162.80")]
    [InlineData("1162.8")]
    [InlineData("1162")]
    public void Create_Accepts_Amount_With_Two_Or_Fewer_Decimals(string raw)
    {
        var value = decimal.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);

        var act = () => Payment.Create(Guid.NewGuid(), D0, value, null, null, null, T0);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Blank_Optional_Fields_Become_Null(string blank)
    {
        var payment = Payment.Create(Guid.NewGuid(), D0, 10.00m, blank, blank, blank, T0);

        payment.Method.Should().BeNull();
        payment.RelatedDocumentNumber.Should().BeNull();
        payment.SourceReference.Should().BeNull();
    }
}
