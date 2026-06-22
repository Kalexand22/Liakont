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
/// empreinte canonique (rupture-de-contrat anchor, anti-doublon PIV04). Les enveloppes de transport
/// (batch, heartbeat) ne sont PAS hashées : leurs composeurs ci-dessous produisent une référence
/// ILLUSTRATIVE du format fil, avec les noms de propriété exacts des DTOs
/// (<see cref="PushBatchRequestDto"/>, <see cref="HeartbeatRequestDto"/>) et les règles de format
/// figées d'ADR-0007. L'encodage fil définitif des enveloppes reste porté par l'ingestion
/// (PIV04/PIV05).</para>
///
/// <para>Les fixtures exercent le modèle pivot COMPLET — à la fois la forme push agent réelle
/// (<c>facture-push-agent-brut</c>, <see cref="PivotLineTaxDto.CategoryCode"/> /
/// <see cref="PivotLineTaxDto.VatexCode"/> nuls, forme hashée par l'anti-doublon PIV04) ET des
/// documents avec les champs de mapping renseignés (<c>CategoryCode</c>/<c>VatexCode</c> peuplés),
/// pour verrouiller les chemins enum/champ-optionnel du sérialiseur cross-runtime. L'agent lui-même
/// ne remplit JAMAIS <c>CategoryCode</c>/<c>VatexCode</c> — cette frontière appartient à la
/// plateforme (lot F03/TVA).</para>
/// </summary>
public static class ContractFixtures
{
    /// <summary>Nom de la fixture du lot mixte (deux documents).</summary>
    public const string BatchFixtureName = "batch-mixte";

    /// <summary>Nom de la fixture de heartbeat.</summary>
    public const string HeartbeatFixtureName = "heartbeat";

    /// <summary>
    /// Version de contrat HYPOTHÉTIQUE servant à matérialiser le seam de cohabitation N/N-1 (RDF08,
    /// ADR-0001/F12 §6.4) AVANT toute rupture réelle : les golden d'enveloppe de
    /// <c>tests/fixtures/contrat-v2/</c> la portent. Ce n'est PAS une version de contrat publiée —
    /// le modèle de payload reste celui de la V1 (aucune rupture métier) ; seul l'axe de version
    /// négociée diffère. Une vraie rupture future ajoutera des golden de DOCUMENT ici et bumpera les
    /// empreintes figées (cf. <c>contrat-agent-v1.md</c> §4).
    /// </summary>
    public const string CohabitationNextVersion = "2";

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
    /// Document pivot ENTIÈREMENT peuplé : chaque propriété publique de chaque DTO pivot porte une
    /// valeur NON par défaut — optionnels et collections inclus, dont <c>PaymentDueDate</c> (BT-9) et
    /// <c>PivotDocumentChargeDto.ReasonCode</c>, que les 8 golden n'exercent pas (RDL02). Données
    /// strictement fictives (CLAUDE.md n°7). SOURCE UNIQUE des gardes de complétude par réflexion :
    /// côté writer (RDL03 y consolidera <c>CanonicalJsonRulesTests</c>) et côté lecteur de PRODUCTION
    /// (<c>PivotCanonicalJsonReader</c> du Pipeline, RDL02). Ce n'est PAS un golden (aucune empreinte
    /// figée) : les gardes reposent sur l'identité du round-trip et la présence par réflexion, jamais
    /// sur un hash gelé.
    /// </summary>
    /// <returns>Un document pivot dont chaque champ est renseigné.</returns>
    public static PivotDocumentDto BuildFullyPopulatedDocument()
    {
        var address = new PivotAddressDto(
            line1: "1 rue de la Paix",
            line2: "Bât B",
            postalCode: "75001",
            city: "Paris",
            countryCode: "FR");

        var supplier = new PivotPartyDto(
            name: "Fournisseur Fictif SA",
            siren: "123456789",
            siret: "12345678900012",
            vatNumber: "FR12345678901",
            address: address,
            email: "contact@fournisseur-fictif.example",
            isCompanyHint: true);

        var customer = new PivotPartyDto(
            name: "Client Fictif SARL",
            siren: "987654321",
            siret: "98765432100099",
            vatNumber: "FR98765432109",
            address: new PivotAddressDto(
                line1: "2 avenue de l'Opéra",
                line2: "Étage 3",
                postalCode: "69001",
                city: "Lyon",
                countryCode: "FR"),
            email: "facturation@client-fictif.example",
            isCompanyHint: true);

        var invoicer = new PivotPartyDto(
            name: "Emetteur Fictif SAS",
            siren: "111222333",
            siret: "11122233300011",
            vatNumber: "FR11122233301",
            address: new PivotAddressDto(
                line1: "3 boulevard Haussmann",
                line2: "Suite 12",
                postalCode: "75009",
                city: "Paris",
                countryCode: "FR"),
            email: "emission@emetteur-fictif.example",
            isCompanyHint: true);

        var payee = new PivotPartyDto(
            name: "Bénéficiaire Fictif SNC",
            siren: "444555666",
            siret: "44455566600044",
            vatNumber: "FR44455566604",
            address: new PivotAddressDto(
                line1: "4 place Vendôme",
                line2: "RDC",
                postalCode: "75001",
                city: "Paris",
                countryCode: "FR"),
            email: "paiement@beneficiaire-fictif.example",
            isCompanyHint: true);

        var totals = new PivotTotalsDto(
            totalNet: 1000.00m,
            totalTax: 200.00m,
            totalGross: 1200.00m,
            sourceTotalGross: 1200.00m);

        var lineTax = new PivotLineTaxDto(
            taxAmount: 200.00m,
            rate: 20.00m,
            categoryCode: VatCategory.S,
            vatexCode: "VATEX-EU-G");

        var line = new PivotLineDto(
            description: "Prestation fictive de test",
            netAmount: 1000.00m,
            quantity: 2m,
            unitPriceNet: 500.00m,
            sourceRegimeCodes: new[] { "TVA_20" },
            taxes: new[] { lineTax },
            sourceLineRef: "LIG-001",
            sourceData: "{\"raw\":\"line\"}",
            unitCode: "C62");

        var creditNoteRef = new PivotDocumentRefDto(
            number: "FA-2026-001",
            issueDate: new DateTime(2026, 1, 15),
            sourceReference: "SRC-FA-001");

        var payment = new PivotPaymentDto(
            paymentDate: new DateTime(2026, 2, 1),
            amount: 1200.00m,
            method: "virement",
            relatedDocumentNumber: "FA-2026-001",
            sourceReference: "PAY-001");

        var documentCharge = new PivotDocumentChargeDto(
            isCharge: true,
            amount: 10.00m,
            reason: "éco-contribution",
            reasonCode: "ECO",
            sourceRegimeCodes: new[] { "ECO_CONTRIB" });

        return new PivotDocumentDto(
            sourceDocumentKind: "FA",
            number: "FA-2026-TEST",
            issueDate: new DateTime(2026, 3, 1),
            sourceReference: "SRC-2026-TEST",
            supplier: supplier,
            totals: totals,
            operationCategory: OperationCategory.PrestationServices,
            currencyCode: "EUR",
            customer: customer,
            lines: new[] { line },
            creditNoteRefs: new[] { creditNoteRef },
            payments: new[] { payment },
            documentCharges: new[] { documentCharge },
            invoicer: invoicer,
            payee: payee,
            isSelfBilled: true,
            prepaidAmount: 100.00m,
            sourceData: "{\"raw\":\"doc\"}",
            paymentDueDate: new DateTime(2026, 3, 31),
            isB2cReportingDeclaration: true,
            sellerFees: new[]
            {
                new PivotSellerFeeDto(
                    lotReference: "no_ba=5000",
                    netAmount: 80.00m,
                    sourceRegimeCode: "MARGE",
                    sourceLineRef: "ligne#bv",
                    description: "Frais vendeur fictif"),
            },
            buyerFees: new[]
            {
                new PivotBuyerFeeDto(
                    lotReference: "no_ba=5000",
                    netAmount: 50.00m,
                    sourceRegimeCode: "MARGE",
                    sourceLineRef: "ligne#fa",
                    description: "Frais acheteur fictif"),
            },
            invoicePeriod: new PivotInvoicePeriodDto(
                startDate: new DateTime(2026, 1, 1),
                endDate: new DateTime(2026, 1, 31)));
    }

    /// <summary>
    /// Compose le JSON d'un lot ILLUSTRATIF (POST /api/agent/v1/documents/batch) portant deux
    /// documents de référence. Les documents embarqués sont sérialisés canoniquement (PIV02) ; le
    /// wrapper <c>{ContractVersion, Documents:[…]}</c> reprend les noms de propriété exacts de
    /// <see cref="PushBatchRequestDto"/>. Référence de format, non hashée (voir résumé de classe).
    /// </summary>
    /// <returns>Le JSON canonique du lot.</returns>
    public static string ComposeBatchRequestJson() => ComposeBatchRequestJson(AgentContractVersion.ContractVersion);

    /// <summary>
    /// Compose le JSON d'un lot ILLUSTRATIF portant la version de contrat <paramref name="contractVersion"/>.
    /// Les documents embarqués sont les MÊMES quelle que soit la version : l'axe de version négociée
    /// (enveloppe) est orthogonal à l'empreinte par document (seul le payload par document est hashé).
    /// Sert à matérialiser le seam N/N-1 (golden v1 = version de l'assembly ; golden v2 =
    /// <see cref="CohabitationNextVersion"/>).
    /// </summary>
    /// <param name="contractVersion">Version de contrat portée par l'enveloppe.</param>
    /// <returns>Le JSON canonique du lot.</returns>
    public static string ComposeBatchRequestJson(string contractVersion)
    {
        string first = CanonicalJson.Serialize(GetDocument("facture-standard-b2c"));
        string second = CanonicalJson.Serialize(GetDocument("avoir-simple-lie"));

        // Enveloppe minimale (deux documents) : assemblée explicitement car le writer canonique ne
        // sait pas réinjecter un sous-document déjà sérialisé. Format figé : pas d'espaces, ordre
        // de déclaration du DTO. La version de contrat est paramétrée (axe de négociation).
        return "{\"ContractVersion\":\"" + contractVersion + "\",\"Documents\":["
            + first + "," + second + "]}";
    }

    /// <summary>
    /// Compose le JSON ILLUSTRATIF d'un heartbeat (POST /api/agent/v1/heartbeat). Reprend les noms
    /// de propriété exacts de <see cref="HeartbeatRequestDto"/> et le format d'horodatage UTC figé
    /// d'ADR-0007 (<c>yyyy-MM-ddTHH:mm:ssZ</c>). Référence de format, non hashée.
    /// </summary>
    /// <returns>Le JSON canonique du heartbeat.</returns>
    public static string ComposeHeartbeatJson() => ComposeHeartbeatJson(AgentContractVersion.ContractVersion);

    /// <summary>
    /// Compose le JSON ILLUSTRATIF d'un heartbeat portant la version de contrat
    /// <paramref name="contractVersion"/>. Identique à <see cref="ComposeHeartbeatJson()"/> hormis la
    /// version (axe de négociation) — sert à matérialiser le seam N/N-1 (golden v2).
    /// </summary>
    /// <param name="contractVersion">Version de contrat portée par l'enveloppe.</param>
    /// <returns>Le JSON canonique du heartbeat.</returns>
    public static string ComposeHeartbeatJson(string contractVersion)
    {
        var heartbeat = new HeartbeatRequestDto(
            contractVersion: contractVersion,
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
            new DocumentFixture("facture-push-agent-brut", BuildFacturePushAgentBrut()),
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

    // ── Cas 8 : forme push agent RÉELLE — CategoryCode/VatexCode nuls (mapping plateforme absent) ──
    // Cette fixture représente le payload EXACT tel qu'un agent l'envoie : seul SourceRegimeCodes est
    // renseigné ; CategoryCode et VatexCode restent nuls. C'est la forme hashée par l'anti-doublon PIV04.
    private static PivotDocumentDto BuildFacturePushAgentBrut()
    {
        var supplier = new PivotPartyDto(
            name: "Étude Fictïve SVV",
            siren: "111111111",
            address: new PivotAddressDto(city: "Rennes", countryCode: "FR"));

        // agent push : pas de catégorie/VATEX mappés
        var tax = new PivotLineTaxDto(taxAmount: 24.00m, rate: 20m);
        var line = new PivotLineDto(
            description: "Adjudication lot 9 — push agent brut",
            netAmount: 120.00m,
            quantity: 1m,
            unitPriceNet: 120.00m,
            sourceRegimeCodes: new[] { "NORMAL" },
            taxes: new[] { tax },
            sourceLineRef: "ligne#1");

        var totals = new PivotTotalsDto(totalNet: 120.00m, totalTax: 24.00m, totalGross: 144.00m, sourceTotalGross: 144.00m);

        return new PivotDocumentDto(
            sourceDocumentKind: "F",
            number: "F-2026-0500",
            issueDate: new DateTime(2026, 1, 12),
            sourceReference: "no_ba=4500",
            supplier: supplier,
            totals: totals,
            operationCategory: OperationCategory.LivraisonBiens,
            lines: new[] { line });
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
