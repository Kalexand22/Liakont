namespace Liakont.PaClients.SuperPdp.Tests.Unit;

using System;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Transmission.Contracts;
using Xunit;

/// <summary>
/// Couvre l'envoi e-reporting B2C de Super PDP (B5, F14 §3.3) : POST <c>/v1.beta/b2c_transactions</c> avec
/// montants en CHAÎNE (l'API attend <c>string (decimal)</c>), <c>id</c> readOnly jamais émis, rattachement
/// de l'id serveur ; et la garde de capacité DÉDIÉE (montant de marge TMA1 → <c>SupportsMarginAmountReporting</c>).
/// Forme de réponse reproduite du contrat RÉEL (✅ sandbox 2026-06-22, id 585).
/// </summary>
public sealed class SuperPdpClientB2cTests
{
    private const string CreatedJson =
        """{"data":[{"id":585,"date":"2026-06-22","currency":"EUR","category_code":"TMA1","tax_exclusive_amount":"137.00","tax_total":"27.40","tax_subtotals":[{"tax_percent":"20.00","taxable_amount":"137.00","tax_total":"27.40"}]}]}""";

    [Fact]
    public async Task Margin_Posts_b2c_transactions_AsStrings_AndAttaches_ServerId()
    {
        var handler = StubHttpMessageHandler.Returns(HttpStatusCode.OK, CreatedJson);
        var client = SuperPdpTestData.CreateClient(handler);

        var result = await client.SendB2cTransactionAsync(MarginTransaction());

        result.State.Should().Be(PaSendState.Issued);
        result.PaDocumentId.Should().Be("585", "l'id serveur de la transaction est rattaché");

        handler.LastRequestUri!.AbsolutePath.Should().EndWith("/v1.beta/b2c_transactions");
        handler.LastRequestBody.Should().Contain("\"category_code\":\"TMA1\"");
        handler.LastRequestBody.Should().Contain("\"role_code\":\"SE\"");
        handler.LastRequestBody.Should().Contain("\"tax_exclusive_amount\":\"137.00\"", "montants en chaîne (string decimal)");
        handler.LastRequestBody.Should().Contain("\"date\":\"2026-06-22\"");
        handler.LastRequestBody.Should().NotContain("\"id\"", "le champ id readOnly n'est jamais émis");
    }

    [Fact]
    public async Task Margin_ToAccountWithoutMarginCapability_IsTypedGap_NoNetworkCall()
    {
        // Régression review (P1) : la marge se garde sur la capacité DÉDIÉE, jamais sur le seul B2cReporting.
        var handler = StubHttpMessageHandler.Returns(HttpStatusCode.OK, CreatedJson);
        var caps = new PaCapabilities
        {
            PaName = "Super PDP",
            SupportsB2cReporting = true,
            SupportsMarginAmountReporting = false,
        };
        var client = SuperPdpTestData.CreateClient(handler, caps);

        var result = await client.SendB2cTransactionAsync(MarginTransaction());

        result.State.Should().Be(PaSendState.CapabilityNotSupported);
        result.CapabilityNotSupported!.Capability.Should().Be(PaCapability.MarginAmountReporting);
        handler.CallCount.Should().Be(0, "capacité absente → aucun appel réseau");
    }

    private static B2cReportingTransaction MarginTransaction() => new()
    {
        Category = EReportingTransactionCategory.Tma1,
        Role = EReportingDeclarantRole.Seller,
        CurrencyCode = "EUR",
        Date = new DateOnly(2026, 6, 22),
        TaxExclusiveAmount = 137.00m,
        TaxTotal = 27.40m,
        Subtotals = [new B2cReportingTransactionSubtotal { TaxPercent = 20.0m, TaxableAmount = 137.00m, TaxTotal = 27.40m }],
    };
}
