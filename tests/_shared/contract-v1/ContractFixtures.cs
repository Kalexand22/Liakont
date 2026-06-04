namespace Liakont.Agent.Contracts.ContractTests;

using System;
using System.Collections.Generic;
using System.Globalization;
using Liakont.Agent.Contracts;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Serialization;
using Liakont.Agent.Contracts.Transport;

// Données golden de test : les petits tableaux littéraux passés aux constructeurs (CA1861) sont
// volontaires — ce fichier EST le document de référence du contrat, l'extraction en champs static
// readonly nuirait à sa lisibilité.
#pragma warning disable CA1861

/// <summary>
/// Builders des fixtures de contrat v1 (PIV03). Ce fichier est LIÉ dans les deux projets de test :
/// la plateforme (.NET 10, <c>src/Liakont.sln</c>) ET l'agent (net48, <c>agent/Liakont.Agent.sln</c>).
/// Il construit, à partir de données STRICTEMENT FICTIVES (aucun SIREN, raison sociale ou référence
/// réels — CLAUDE.md n°7), le jeu de documents pivot représentatifs servant de golden files
/// (<c>tests/fixtures/contrat-v1/</c>). La sérialisation canonique de chaque document, produite par
/// l'UNIQUE <see cref="CanonicalJson"/> (PIV02), doit être identique octet par octet entre net48 et
/// .NET 10 — c'est ce que prouvent les tests de <see cref="ContractFixtureTests"/> exécutés des deux
/// côtés (F12 §3.4, ADR-0007).
///
/// <para>Frontière hash : seul le PAYLOAD PAR DOCUMENT (<see cref="PivotDocumentDto"/>) porte une
/// empreinte canonique (anti-doublon PIV04). Les enveloppes de transport (batch, heartbeat) ne sont
/// PAS hashées : leurs composeurs ci-dessous produisent une référence ILLUSTRATIVE du format fil,
/// avec les noms de propriété exacts des DTOs (<see cref="PushBatchRequestDto"/>,
/// <see cref="HeartbeatRequestDto"/>) et les règles de format figées d'ADR-0007. L'encodage fil
/// définitif des enveloppes reste porté par l'ingestion (PIV04/PIV05).</para>
/// </summary>
public static class ContractFixtures
{
    /// <summary>Nom de la fixture du lot mixte (deux documents).</summary>
    public const string BatchFixtureName = "batch-mixte";

    /// <summary>Nom de la fixture de heartbeat.</summary>
    public const string HeartbeatFixtureName = "heartbeat";

    private static readonly IReadOnlyList<DocumentFixture> DocumentsBacking = BuildDocuments();

    /// <summary>Les documents pivot de référence (données fictives), dans un ordre stable.</summary>
    public static IReadOnlyList<DocumentFixture> Documents => DocumentsBacking;

    /// <summary>Cas de test xUnit : un nom de fixture document par cas.</summary>
    public static IEnumerable<object[]> DocumentCases
    {
        get
        {
            foreach (DocumentFixture fixture in DocumentsBacking)
            {
                yield return new object[] { fixture.Name };
            }
        }
    }

    /// <summary>Retrouve un document de référence par son nom.</summary>
    /// <param name="name">Nom de la fixture (cf. <see cref="DocumentFixture.Name"/>).</param>
    /// <returns>Le document pivot correspondant.</returns>
    public static PivotDocumentDto GetDocument(string name)
    {
        foreach (DocumentFixture fixture in DocumentsBacking)
        {
            if (string.Equals(fixture.Name, name, StringComparison.Ordinal))
            {
                return fixture.Document;
            }
        }

        throw new ArgumentException("Fixture document inconnue : " + name, nameof(name));
    }

    /// <summary>
    /// Compose le JSON d'un lot ILLUSTRATIF (POST /api/agent/v1/documents/batch) portant deux
    /// documents de référence. Les documents embarqués sont sérialisés canoniquement (PIV02) ; le
    /// wrapper <c>{ContractVersion, Documents:[…]}</c> reprend les noms de propriété exacts de
    /// <see cref="PushBatchRequestDto"/>. Référence de format, non hashée (voir résumé de classe).
    /// </summary>
    /// <returns>Le JSON canonique du lot.</returns>
    public static string ComposeBatchRequestJson()
    {
        string first = CanonicalJson.Serialize(GetDocument("facture-standard-b2c"));
        string second = CanonicalJson.Serialize(GetDocument("avoir-simple-lie"));

        // Enveloppe minimale (deux documents) : assemblée explicitement car le writer canonique ne
        // sait pas réinjecter un sous-document déjà sérialisé. Format figé : pas d'espaces, ordre
        // de déclaration du DTO, version de contrat de l'assembly.
        return "{\"ContractVersion\":\"" + AgentContractVersion.ContractVersion + "\",\"Documents\":["
            + first + "," + second + "]}";
    }

    /// <summary>
    /// Compose le JSON ILLUSTRATIF d'un heartbeat (POST /api/agent/v1/heartbeat). Reprend les noms
    /// de propriété exacts de <see cref="HeartbeatRequestDto"/> et le format d'horodatage UTC figé
    /// d'ADR-0007 (<c>yyyy-MM-ddTHH:mm:ssZ</c>). Référence de format, non hashée.
    /// </summary>
    /// <returns>Le JSON canonique du heartbeat.</returns>
    public static string ComposeHeartbeatJson()
    {
        var heartbeat = new HeartbeatRequestDto(
            contractVersion: AgentContractVersion.ContractVersion,
            agentVersion: "2.4.0",
            sentAtUtc: new DateTime(2026, 2, 1, 9, 30, 0, DateTimeKind.Utc),
            lastSuccessfulSyncUtc: new DateTime(2026, 1, 31, 22, 15, 0, DateTimeKind.Utc));

        var writer = new CanonicalJsonWriter();
        writer.BeginObject();
        writer.WritePropertyName("ContractVersion");
        writer.WriteString(heartbeat.ContractVersion);
        writer.WritePropertyName("AgentVersion");
        writer.WriteString(heartbeat.AgentVersion);
        writer.WritePropertyName("SentAtUtc");
        writer.WriteString(FormatTimestamp(heartbeat.SentAtUtc));
        if (heartbeat.LastSuccessfulSyncUtc.HasValue)
        {
            writer.WritePropertyName("LastSuccessfulSyncUtc");
            writer.WriteString(FormatTimestamp(heartbeat.LastSuccessfulSyncUtc.Value));
        }

        writer.EndObject();
        return writer.ToString();
    }

    private static string FormatTimestamp(DateTime value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static List<DocumentFixture> BuildDocuments() =>
        new List<DocumentFixture>
        {
            new DocumentFixture("facture-standard-b2c", BuildFactureStandardB2C()),
            new DocumentFixture("vente-sur-marge-exoneree", BuildVenteSurMargeExoneree()),
            new DocumentFixture("avoir-simple-lie", BuildAvoirSimpleLie()),
            new DocumentFixture("avoir-partiel", BuildAvoirPartiel()),
            new DocumentFixture("avoir-groupe-multi-refs", BuildAvoirGroupeMultiRefs()),
            new DocumentFixture("facture-b2b-pro", BuildFactureB2BPro()),
            new DocumentFixture("facture-prestation-paiements", BuildFacturePrestationPaiements()),
        };

    // ── Cas 1 : facture B2C standard, taux normal (le cas le plus fréquent ~20%) ──
    private static PivotDocumentDto BuildFactureStandardB2C()
    {
        var supplier = new PivotPartyDto(
            name: "Étude Fictïve SVV",
            siren: "111111111",
            address: new PivotAddressDto(line1: "3 quai des Brumes", postalCode: "35000", city: "Rennes", countryCode: "FR"));

        var tax = new PivotLineTaxDto(taxAmount: 24.00m, rate: 20m, categoryCode: VatCategory.S);
        var line = new PivotLineDto(
            description: "Adjudication lot 7 — vase décoratif",
            netAmount: 120.00m,
            quantity: 1m,
            unitPriceNet: 120.00m,
            sourceRegimeCodes: new[] { "NORMAL" },
            taxes: new[] { tax },
            sourceLineRef: "ligne#1");

        var totals = new PivotTotalsDto(totalNet: 120.00m, totalTax: 24.00m, totalGross: 144.00m, sourceTotalGross: 144.00m);

        return new PivotDocumentDto(
            sourceDocumentKind: "F",
            number: "F-2026-0007",
            issueDate: new DateTime(2026, 1, 10),
            sourceReference: "no_ba=4007",
            supplier: supplier,
            totals: totals,
            operationCategory: OperationCategory.LivraisonBiens,
            lines: new[] { line });
    }

    // ── Cas 2 : vente sur marge exonérée (catégorie E + VATEX-EU-J, taux 0) ──
    private static PivotDocumentDto BuildVenteSurMargeExoneree()
    {
        var supplier = new PivotPartyDto(
            name: "Brocante Légère SARL",
            siren: "222222222",
            address: new PivotAddressDto(city: "Nantes", countryCode: "FR"));

        var tax = new PivotLineTaxDto(taxAmount: 0m, rate: 0m, categoryCode: VatCategory.E, vatexCode: "VATEX-EU-J");
        var line = new PivotLineDto(
            description: "Tableau ancien — régime de la marge",
            netAmount: 800.00m,
            quantity: 1m,
            unitPriceNet: 800.00m,
            sourceRegimeCodes: new[] { "MARGE" },
            taxes: new[] { tax },
            sourceLineRef: "ligne#1");

        var totals = new PivotTotalsDto(totalNet: 800.00m, totalTax: 0m, totalGross: 800.00m, sourceTotalGross: 800.00m);

        return new PivotDocumentDto(
            sourceDocumentKind: "F",
            number: "F-2026-0042",
            issueDate: new DateTime(2026, 1, 14),
            sourceReference: "no_ba=4042",
            supplier: supplier,
            totals: totals,
            operationCategory: OperationCategory.LivraisonBiens,
            lines: new[] { line });
    }

    // ── Cas 3 : avoir simple lié à une seule facture d'origine ──
    private static PivotDocumentDto BuildAvoirSimpleLie()
    {
        var supplier = new PivotPartyDto(
            name: "Étude Fictïve SVV",
            siren: "111111111",
            address: new PivotAddressDto(city: "Rennes", countryCode: "FR"));

        var tax = new PivotLineTaxDto(taxAmount: -24.00m, rate: 20m, categoryCode: VatCategory.S);
        var line = new PivotLineDto(
            description: "Annulation adjudication lot 7",
            netAmount: -120.00m,
            quantity: 1m,
            unitPriceNet: 120.00m,
            sourceRegimeCodes: new[] { "NORMAL" },
            taxes: new[] { tax },
            sourceLineRef: "ligne#1");

        var totals = new PivotTotalsDto(totalNet: -120.00m, totalTax: -24.00m, totalGross: -144.00m, sourceTotalGross: -144.00m);

        var refs = new[] { new PivotDocumentRefDto("F-2026-0007", new DateTime(2026, 1, 10), sourceReference: "no_ba=4007") };

        return new PivotDocumentDto(
            sourceDocumentKind: "A",
            number: "AV-2026-0003",
            issueDate: new DateTime(2026, 1, 20),
            sourceReference: "no_ba=5003",
            supplier: supplier,
            totals: totals,
            operationCategory: OperationCategory.LivraisonBiens,
            lines: new[] { line },
            creditNoteRefs: refs);
    }

    // ── Cas 4 : avoir partiel (remboursement d'une partie seulement de la facture) ──
    private static PivotDocumentDto BuildAvoirPartiel()
    {
        var supplier = new PivotPartyDto(
            name: "Brocante Légère SARL",
            siren: "222222222",
            address: new PivotAddressDto(city: "Nantes", countryCode: "FR"));

        var tax = new PivotLineTaxDto(taxAmount: -8.00m, rate: 20m, categoryCode: VatCategory.S);
        var line = new PivotLineDto(
            description: "Remise commerciale partielle lot 12",
            netAmount: -40.00m,
            quantity: 1m,
            unitPriceNet: 40.00m,
            sourceRegimeCodes: new[] { "NORMAL" },
            taxes: new[] { tax },
            sourceLineRef: "ligne#1");

        var totals = new PivotTotalsDto(totalNet: -40.00m, totalTax: -8.00m, totalGross: -48.00m, sourceTotalGross: -48.00m);

        var refs = new[] { new PivotDocumentRefDto("F-2026-0042", new DateTime(2026, 1, 14)) };

        return new PivotDocumentDto(
            sourceDocumentKind: "A",
            number: "AV-2026-0005",
            issueDate: new DateTime(2026, 1, 22),
            sourceReference: "no_ba=5005",
            supplier: supplier,
            totals: totals,
            operationCategory: OperationCategory.LivraisonBiens,
            lines: new[] { line },
            creditNoteRefs: refs,
            sourceData: "{\"motif\":\"remise négociée\",\"taux_remise\":\"10%\"}");
    }

    // ── Cas 5 : avoir groupé portant plusieurs factures d'origine (multi-références) ──
    private static PivotDocumentDto BuildAvoirGroupeMultiRefs()
    {
        var supplier = new PivotPartyDto(
            name: "Encans du Ponant SAS",
            siren: "333333333",
            address: new PivotAddressDto(city: "Brest", countryCode: "FR"));

        var tax = new PivotLineTaxDto(taxAmount: -60.00m, rate: 20m, categoryCode: VatCategory.S);
        var line = new PivotLineDto(
            description: "Régularisation groupée trois ventes",
            netAmount: -300.00m,
            quantity: 1m,
            unitPriceNet: 300.00m,
            sourceRegimeCodes: new[] { "NORMAL" },
            taxes: new[] { tax },
            sourceLineRef: "ligne#1");

        var totals = new PivotTotalsDto(totalNet: -300.00m, totalTax: -60.00m, totalGross: -360.00m, sourceTotalGross: -360.00m);

        var refs = new[]
        {
            new PivotDocumentRefDto("F-2026-0101", new DateTime(2026, 1, 5)),
            new PivotDocumentRefDto("F-2026-0102", new DateTime(2026, 1, 6), sourceReference: "no_ba=4102"),
            new PivotDocumentRefDto("F-2026-0103", new DateTime(2026, 1, 7)),
        };

        return new PivotDocumentDto(
            sourceDocumentKind: "A",
            number: "AV-2026-0011",
            issueDate: new DateTime(2026, 1, 25),
            sourceReference: "no_ba=5011",
            supplier: supplier,
            totals: totals,
            operationCategory: OperationCategory.LivraisonBiens,
            lines: new[] { line },
            creditNoteRefs: refs);
    }

    // ── Cas 6 : facture B2B (acheteur professionnel identifié) — multi-lignes, multi-taxes, charge ──
    private static PivotDocumentDto BuildFactureB2BPro()
    {
        var supplier = new PivotPartyDto(
            name: "Encans du Ponant SAS",
            siren: "333333333",
            vatNumber: "FR33333333333",
            address: new PivotAddressDto(line1: "12 rue de Siam", postalCode: "29200", city: "Brest", countryCode: "FR"));

        var customer = new PivotPartyDto(
            name: "Antiquités Lépine & Cie",
            siren: "444444444",
            vatNumber: "FR44444444444",
            address: new PivotAddressDto(line1: "5 place Graslin", postalCode: "44000", city: "Nantes", countryCode: "FR"),
            isCompanyHint: true);

        var ligneBien = new PivotLineDto(
            description: "Lot mobilier XIXe",
            netAmount: 1000.00m,
            quantity: 1m,
            unitPriceNet: 1000.00m,
            sourceRegimeCodes: new[] { "NORMAL" },
            taxes: new[] { new PivotLineTaxDto(taxAmount: 200.00m, rate: 20m, categoryCode: VatCategory.S) },
            sourceLineRef: "ligne#1");

        var ligneService = new PivotLineDto(
            description: "Frais de catalogue (taux réduit)",
            netAmount: 100.00m,
            quantity: 2m,
            unitPriceNet: 50.00m,
            sourceRegimeCodes: new[] { "REDUIT" },
            taxes: new[] { new PivotLineTaxDto(taxAmount: 5.50m, rate: 5.5m, categoryCode: VatCategory.AA) },
            sourceLineRef: "ligne#2");

        var charges = new[]
        {
            new PivotDocumentChargeDto(isCharge: true, amount: 1.20m, reason: "éco-contribution mobilier", sourceRegimeCodes: new[] { "ECO" }),
        };

        var totals = new PivotTotalsDto(totalNet: 1100.00m, totalTax: 205.50m, totalGross: 1305.50m, sourceTotalGross: 1305.50m);

        return new PivotDocumentDto(
            sourceDocumentKind: "F",
            number: "F-2026-0205",
            issueDate: new DateTime(2026, 1, 28),
            sourceReference: "no_ba=4205",
            supplier: supplier,
            totals: totals,
            operationCategory: OperationCategory.Mixte,
            customer: customer,
            lines: new[] { ligneBien, ligneService },
            documentCharges: charges);
    }

    // ── Cas 7 : prestation de services avec encaissements (F09), auto-facturation et affacturage ──
    private static PivotDocumentDto BuildFacturePrestationPaiements()
    {
        var supplier = new PivotPartyDto(
            name: "Conseil Légal Fictif EURL",
            siren: "555555555",
            vatNumber: "FR55555555555",
            address: new PivotAddressDto(city: "Quimper", countryCode: "FR"));

        var invoicer = new PivotPartyDto(name: "Mandataire Facturation Fictif SAS", siren: "666666666");
        var payee = new PivotPartyDto(name: "Affactureur Fictif SA", siren: "777777777");

        var tax = new PivotLineTaxDto(taxAmount: 100.00m, rate: 20m, categoryCode: VatCategory.S);
        var line = new PivotLineDto(
            description: "Honoraires de conseil — janvier",
            netAmount: 500.00m,
            quantity: 1m,
            unitPriceNet: 500.00m,
            sourceRegimeCodes: new[] { "NORMAL" },
            taxes: new[] { tax },
            sourceLineRef: "ligne#1");

        var totals = new PivotTotalsDto(totalNet: 500.00m, totalTax: 100.00m, totalGross: 600.00m, sourceTotalGross: 600.00m);

        var payments = new[]
        {
            new PivotPaymentDto(new DateTime(2026, 2, 5), 300.00m, method: "virement", relatedDocumentNumber: "F-2026-0301", sourceReference: "enc#1"),
            new PivotPaymentDto(new DateTime(2026, 2, 20), 300.00m, method: "virement", relatedDocumentNumber: "F-2026-0301", sourceReference: "enc#2"),
        };

        return new PivotDocumentDto(
            sourceDocumentKind: "F",
            number: "F-2026-0301",
            issueDate: new DateTime(2026, 2, 1),
            sourceReference: "no_ba=4301",
            supplier: supplier,
            totals: totals,
            operationCategory: OperationCategory.PrestationServices,
            lines: new[] { line },
            payments: payments,
            invoicer: invoicer,
            payee: payee,
            isSelfBilled: true,
            prepaidAmount: 150.00m);
    }

    /// <summary>Une fixture document : son nom (= nom de fichier golden) et le document pivot.</summary>
    public sealed class DocumentFixture
    {
        /// <summary>Crée une fixture document.</summary>
        /// <param name="name">Nom de la fixture (sans extension).</param>
        /// <param name="document">Le document pivot de référence.</param>
        public DocumentFixture(string name, PivotDocumentDto document)
        {
            Name = name;
            Document = document;
        }

        /// <summary>Nom de la fixture (= nom du fichier golden, sans extension).</summary>
        public string Name { get; }

        /// <summary>Le document pivot de référence.</summary>
        public PivotDocumentDto Document { get; }
    }
}
