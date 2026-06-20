namespace Liakont.Agent.Contracts.ContractTests;

using System;
using System.Linq;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Serialization;
using Xunit;

// Données golden de test : les petits tableaux littéraux passés au constructeur (CA1861) sont
// volontaires — extraire des champs static readonly nuirait à la lisibilité du document de référence.
#pragma warning disable CA1861

/// <summary>
/// Tests de contrat CROSS-RUNTIME du sérialiseur canonique (PIV02). Ce fichier est LIÉ dans les deux
/// projets de test : la plateforme (.NET 10, <c>src/Liakont.sln</c>) ET l'agent (net48,
/// <c>agent/Liakont.Agent.sln</c>). Le MÊME document golden (un avoir complet : lignes, taxes,
/// références, paiements, charges, montants négatifs, caractères non-ASCII), sérialisé par l'UNIQUE
/// writer partagé, doit produire la MÊME empreinte des deux côtés. L'empreinte figée
/// <see cref="GoldenAvoirSha256"/> est l'ancre d'identité : si net48 et .NET 10 divergeaient d'un
/// seul octet — ou en cas de régression de format — ce test casse (F12 §3.4, ADR-0007). PIV03
/// étendra ces golden à un jeu de fixtures complet.
/// </summary>
public sealed class PivotContractGoldenTests
{
    // Empreinte SHA-256 (hex minuscule) figée du JSON canonique de l'avoir golden.
    private const string GoldenAvoirSha256 = "254e8ef94fe74d017c3c172af7c04ce89118d6efd128512bc9604529e347075c";

    /// <summary>Construit le document golden « avoir complet » (données FICTIVES uniquement, SANS échéance).</summary>
    /// <returns>Le document pivot de référence.</returns>
    public static PivotDocumentDto BuildAvoirComplet() => BuildAvoir(paymentDueDate: null);

    /// <summary>Le même document golden, mais portant une date d'échéance de paiement (EN 16931 BT-9).</summary>
    /// <param name="paymentDueDate">La date d'échéance à porter.</param>
    /// <returns>Le document pivot de référence enrichi de BT-9.</returns>
    public static PivotDocumentDto BuildAvoirCompletAvecEcheance(DateTime paymentDueDate) =>
        BuildAvoir(paymentDueDate);

    /// <summary>Le même document golden, mais portant une période de facturation (EN 16931 BG-14, RD406).</summary>
    /// <param name="invoicePeriod">La période de facturation à porter.</param>
    /// <returns>Le document pivot de référence enrichi de BG-14.</returns>
    public static PivotDocumentDto BuildAvoirCompletAvecPeriode(PivotInvoicePeriodDto invoicePeriod) =>
        BuildAvoir(paymentDueDate: null, invoicePeriod: invoicePeriod);

    /// <summary>
    /// Construit l'avoir golden avec une échéance de paiement (BT-9) et une période de facturation (BG-14)
    /// optionnelles — base partagée des fabriques publiques (les DEUX nuls pour l'ancre golden figée).
    /// </summary>
    /// <param name="paymentDueDate">L'échéance à porter, ou <c>null</c> pour le golden de référence figé.</param>
    /// <param name="invoicePeriod">La période de facturation à porter, ou <c>null</c> pour le golden de référence figé.</param>
    /// <returns>Le document pivot golden.</returns>
    public static PivotDocumentDto BuildAvoir(DateTime? paymentDueDate, PivotInvoicePeriodDto? invoicePeriod = null)
    {
        var supplier = new PivotPartyDto(
            name: "Galerie Fictïve SARL",
            siren: "111111111",
            address: new PivotAddressDto(city: "Rennes", countryCode: "FR"));

        var customer = new PivotPartyDto(name: "Acheteur Pro Fictif", isCompanyHint: true);

        var totals = new PivotTotalsDto(
            totalNet: -1000.00m,
            totalTax: 0m,
            totalGross: -1000.00m,
            sourceTotalGross: -1000.0m);

        var tax = new PivotLineTaxDto(taxAmount: 0m, rate: 0m, categoryCode: VatCategory.E, vatexCode: "VATEX-EU-J");

        var line = new PivotLineDto(
            description: "Adjudication lot 12 — éco",
            netAmount: -1000.00m,
            quantity: 1m,
            unitPriceNet: 1000.00m,
            sourceRegimeCodes: new[] { "6", "MARGE" },
            taxes: new[] { tax },
            sourceLineRef: "ligne#4");

        var creditNoteRefs = new[]
        {
            new PivotDocumentRefDto("F-2026-001", new DateTime(2026, 1, 15)),
            new PivotDocumentRefDto("F-2026-002", new DateTime(2026, 1, 16), sourceReference: "no_ba=99"),
        };

        var payments = new[] { new PivotPaymentDto(new DateTime(2026, 2, 2), 1000.00m, method: "CB") };

        var charges = new[]
        {
            new PivotDocumentChargeDto(isCharge: true, amount: 12.50m, reason: "éco-contribution", sourceRegimeCodes: new[] { "ECO" }),
        };

        return new PivotDocumentDto(
            sourceDocumentKind: "A",
            number: "AV-2026-009",
            issueDate: new DateTime(2026, 2, 1),
            sourceReference: "no_ba=5000",
            supplier: supplier,
            totals: totals,
            operationCategory: OperationCategory.Mixte,
            customer: customer,
            lines: new[] { line },
            creditNoteRefs: creditNoteRefs,
            payments: payments,
            documentCharges: charges,
            isSelfBilled: true,
            prepaidAmount: 300m,
            sourceData: "{\"raw\":true,\"path\":\"C:\\x\"}",
            paymentDueDate: paymentDueDate,
            invoicePeriod: invoicePeriod);
    }

    [Fact]
    public void Golden_avoir_hash_is_stable_and_identical_across_runtimes()
    {
        string hash = PayloadHasher.ComputeHash(BuildAvoirComplet());

        hash.Should().Be(
            GoldenAvoirSha256,
            "le JSON canonique doit être identique octet par octet entre net48 et .NET 10 (F12 §3.4)");
    }

    [Fact]
    public void Hash_is_64_lowercase_hexadecimal_characters()
    {
        string hash = PayloadHasher.ComputeHash(BuildAvoirComplet());

        hash.Should().MatchRegex("^[0-9a-f]{64}$", "SHA-256 en hexadécimal minuscule, 64 caractères");
    }

    [Fact]
    public void Canonical_json_is_pure_ascii()
    {
        string json = CanonicalJson.Serialize(BuildAvoirComplet());

        json.All(c => c >= ' ' && c <= '~').Should().BeTrue(
            "la sortie est ASCII pur : tout caractère non-ASCII est échappé \\uXXXX (ADR-0007)");
    }

    [Fact]
    public void Serialization_and_hash_are_deterministic()
    {
        var document = BuildAvoirComplet();

        CanonicalJson.Serialize(document).Should().Be(CanonicalJson.Serialize(document));
        PayloadHasher.ComputeHash(document).Should().Be(PayloadHasher.ComputeHash(document));
    }

    [Fact]
    public void PaymentDueDate_when_absent_keeps_the_canonical_json_and_hash_byte_identical()
    {
        // Non-régression EXT01 : un document SANS échéance (BT-9) doit produire le JSON canonique et le
        // hash STRICTEMENT inchangés (champ optionnel omis, ADR-0007) — sinon la réconciliation des
        // documents déjà stagés serait invalidée. L'ancre golden est figée sur l'avoir sans échéance.
        var sansEcheance = BuildAvoirComplet();

        string json = CanonicalJson.Serialize(sansEcheance);

        json.Should().NotContain("PaymentDueDate", "un optionnel null n'est jamais émis (le hash doit rester figé)");
        PayloadHasher.ComputeHash(sansEcheance).Should().Be(
            GoldenAvoirSha256, "l'ajout de BT-9 ne doit RIEN changer pour un document qui ne le porte pas");
    }

    [Fact]
    public void PaymentDueDate_when_present_is_emitted_last_and_changes_the_hash()
    {
        var avecEcheance = BuildAvoirCompletAvecEcheance(new DateTime(2026, 3, 15));

        string json = CanonicalJson.Serialize(avecEcheance);

        // BT-9 est émise en FIN d'objet (champ additif, ADR-0007), au format yyyy-MM-dd.
        json.Should().EndWith("\"PaymentDueDate\":\"2026-03-15\"}", "BT-9 est le dernier membre du contrat");

        // Round-trip sans perte ET le hash DIFFÈRE du golden (preuve que le champ est réellement sérialisé).
        var rebuilt = PivotCanonicalReader.ReadDocument(json);
        rebuilt.PaymentDueDate.Should().Be(new DateTime(2026, 3, 15));
        CanonicalJson.Serialize(rebuilt).Should().Be(json, "round-trip sans perte avec l'échéance portée");
        PayloadHasher.ComputeHash(avecEcheance).Should().NotBe(
            GoldenAvoirSha256, "porter BT-9 change le contenu, donc l'empreinte");
    }

    [Fact]
    public void InvoicePeriod_when_absent_keeps_the_canonical_json_and_hash_byte_identical()
    {
        // Slot réservé BG-14 (RD406) : un document SANS période de facturation doit produire le JSON
        // canonique et le hash STRICTEMENT inchangés (champ optionnel omis, ADR-0007) — l'ancre golden
        // est figée sur l'avoir sans période, comme pour BT-9.
        var sansPeriode = BuildAvoirComplet();

        string json = CanonicalJson.Serialize(sansPeriode);

        json.Should().NotContain("InvoicePeriod", "un optionnel null n'est jamais émis (le hash doit rester figé)");
        PayloadHasher.ComputeHash(sansPeriode).Should().Be(
            GoldenAvoirSha256, "réserver le slot BG-14 ne doit RIEN changer pour un document qui ne le porte pas");
    }

    [Fact]
    public void InvoicePeriod_when_present_is_emitted_last_and_changes_the_hash()
    {
        var avecPeriode = BuildAvoirCompletAvecPeriode(
            new PivotInvoicePeriodDto(new DateTime(2026, 1, 1), new DateTime(2026, 1, 31)));

        string json = CanonicalJson.Serialize(avecPeriode);

        // BG-14 est émise en FIN d'objet (champ additif, ADR-0007), StartDate (BT-73) puis EndDate (BT-74).
        json.Should().EndWith(
            "\"InvoicePeriod\":{\"StartDate\":\"2026-01-01\",\"EndDate\":\"2026-01-31\"}}",
            "BG-14 est le dernier membre du contrat (réservé en fin, RD406)");

        // Round-trip sans perte ET le hash DIFFÈRE du golden (preuve que le slot est réellement sérialisé).
        var rebuilt = PivotCanonicalReader.ReadDocument(json);
        rebuilt.InvoicePeriod.Should().NotBeNull();
        rebuilt.InvoicePeriod!.StartDate.Should().Be(new DateTime(2026, 1, 1));
        rebuilt.InvoicePeriod.EndDate.Should().Be(new DateTime(2026, 1, 31));
        CanonicalJson.Serialize(rebuilt).Should().Be(json, "round-trip sans perte avec la période portée");
        PayloadHasher.ComputeHash(avecPeriode).Should().NotBe(
            GoldenAvoirSha256, "porter BG-14 change le contenu, donc l'empreinte");
    }

    [Fact]
    public void Round_trip_is_lossless()
    {
        var document = BuildAvoirComplet();
        string json = CanonicalJson.Serialize(document);

        var rebuilt = PivotCanonicalReader.ReadDocument(json);

        CanonicalJson.Serialize(rebuilt).Should().Be(
            json,
            "désérialiser puis re-sérialiser doit être stable (round-trip sans perte, acceptance PIV02)");
        PayloadHasher.ComputeHash(rebuilt).Should().Be(GoldenAvoirSha256);

        // Contrôles ponctuels : échelle decimal préservée, dates, multi-références, catégorie TVA.
        rebuilt.Totals.TotalNet.Should().Be(-1000.00m);
        rebuilt.Totals.SourceTotalGross.Should().Be(-1000.0m);
        rebuilt.CreditNoteRefs.Should().HaveCount(2);
        rebuilt.CreditNoteRefs[1].IssueDate.Should().Be(new DateTime(2026, 1, 16));
        rebuilt.CreditNoteRefs[1].SourceReference.Should().Be("no_ba=99");
        rebuilt.Lines[0].Taxes[0].CategoryCode.Should().Be(VatCategory.E);
        rebuilt.SourceData.Should().Be("{\"raw\":true,\"path\":\"C:\\x\"}");
    }
}
