namespace Liakont.Modules.Pipeline.Tests.Integration.Check;

using System;
using System.Globalization;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Serialization;
using Liakont.Modules.Ingestion.Contracts.Events;
using Stratum.Common.Abstractions.Events;

/// <summary>Fabriques de données pour les tests d'intégration du CHECK (pivot, empreinte, événement).</summary>
internal static class CheckIntegrationFixtures
{
    private static readonly string[] NormalRegimeCodes = ["NORMAL"];

    public static string PayloadHashOf(PivotDocumentDto pivot) => PayloadHasher.ComputeHash(CanonicalJson.Serialize(pivot));

    public static PivotDocumentDto BuildPivot(
        string sourceReference,
        string regimeCode,
        PivotPartyDto? customer = null,
        OperationCategory? operationCategory = OperationCategory.LivraisonBiens)
    {
        var line = new PivotLineDto(
            description: "Adjudication lot 7 — vase décoratif",
            netAmount: 120.00m,
            quantity: 1m,
            unitPriceNet: 120.00m,
            sourceRegimeCodes: new[] { regimeCode },
            taxes: new[] { new PivotLineTaxDto(24.00m, 20m) },
            sourceLineRef: "ligne#1");

        // operationCategory NULLABLE (B2C02) : le défaut (LivraisonBiens) préserve tous les appels existants ;
        // un appelant passe `null` pour exercer la suspension « nature d'opération non paramétrée » du CHECK
        // (la plateforme remplit la nature à l'ingestion depuis le paramétrage fiscal — absente = bloqué,
        // jamais devinée, CLAUDE.md n°2/n°3 ; cf. DocumentCheckEvaluator.OperationCategoryMissingReason).
        //
        // Acheteur par défaut B2B (à SIREN) : ce pivot « facture ordinaire » exerce la VOIE DOCUMENT (e-invoicing),
        // qui est par construction du B2B (acheteur identifié — cf. [[b2c-egale-ereporting-partout]]). Un acheteur
        // SANS SIREN serait un B2C → e-reporting (différé de la voie document), ce qui n'est PAS ce que testent les
        // scénarios de transmission par-document. Un test B2C explicite passe son propre `customer` anonyme ou
        // utilise une fixture B2C dédiée (BuildPlainTaxableInvoice / BuildB2cReportingDeclaration).
        return new PivotDocumentDto(
            sourceDocumentKind: "F",
            number: "F-2026-" + ((uint)sourceReference.GetHashCode(StringComparison.Ordinal)).ToString("D10", CultureInfo.InvariantCulture),
            issueDate: new DateTime(2026, 1, 10),
            sourceReference: sourceReference,
            supplier: new PivotPartyDto("Étude Fictïve SVV"),
            totals: new PivotTotalsDto(120.00m, 24.00m, 144.00m, 144.00m),
            operationCategory: operationCategory,
            customer: customer ?? BusinessBuyer(),
            lines: new[] { line });
    }

    /// <summary>
    /// Acheteur PROFESSIONNEL identifié (SIREN) — un B2B émettable par la voie document (e-invoicing). Sert de
    /// défaut aux fixtures de transmission par-document (un B2C anonyme serait, lui, différé vers l'e-reporting).
    /// SIREN fictif Luhn-valide (CLAUDE.md n°7).
    /// </summary>
    public static PivotPartyDto BusinessBuyer() =>
        new("Acheteur Pro SARL", siren: "945678902", address: new PivotAddressDto(city: "Nantes", countryCode: "FR"));

    /// <summary>
    /// Acheteur présentant un indice « professionnel » (champ société + forme juridique « SARL ») — déclenche
    /// le garde-fou B2B/B2C (VAL05) SANS dépendre du mapping TVA. Aucun SIREN/pays : seul ce garde-fou bloque.
    /// </summary>
    public static PivotPartyDto ProfessionalBuyer() => new("Client SARL", isCompanyHint: true);

    /// <summary>
    /// Construit une AUTO-FACTURE sous mandat (<c>IsSelfBilled</c>, MND07) : le <c>Supplier</c> EST le mandant
    /// (vendeur fiscal BG-4 → BT-30/BT-31), l'<c>Invoicer</c> est le tenant mandataire qui émet matériellement
    /// (art. 289 I-2 CGI). Le numéro SOURCE est distinct du BT-1 fiscal (alloué séparément par mandant — MND05).
    /// Valeurs fictives (CLAUDE.md n°7).
    /// </summary>
    public static PivotDocumentDto BuildSelfBilledPivot(string sourceReference, string mandantSiren, string? mandantVatNumber)
    {
        var line = new PivotLineDto(
            description: "Vente sous mandat — lot criée",
            netAmount: 120.00m,
            quantity: 1m,
            unitPriceNet: 120.00m,
            sourceRegimeCodes: NormalRegimeCodes,
            taxes: new[] { new PivotLineTaxDto(24.00m, 20m) },
            sourceLineRef: "ligne#1");

        return new PivotDocumentDto(
            sourceDocumentKind: "F",
            number: "SRC-" + ((uint)sourceReference.GetHashCode(StringComparison.Ordinal)).ToString("D10", CultureInfo.InvariantCulture),
            issueDate: new DateTime(2026, 1, 10),
            sourceReference: sourceReference,
            supplier: new PivotPartyDto("Armement Mandant Fictif", siren: mandantSiren, vatNumber: mandantVatNumber),
            totals: new PivotTotalsDto(120.00m, 24.00m, 144.00m, 144.00m),
            operationCategory: OperationCategory.LivraisonBiens,
            customer: BusinessBuyer(),
            lines: new[] { line },
            invoicer: new PivotPartyDto("Étude Mandataire Fictïve", siren: "404833048"),
            isSelfBilled: true);
    }

    /// <summary>
    /// Construit un AVOIR (pivot) référençant une ou plusieurs factures d'origine — montants POSITIFS (la nature
    /// « avoir » est portée par le type, jamais par le signe, F07-F08 §B.2). Le code régime est explicite
    /// (« NORMAL » = mappé sur la table validée des harnais ; un code absent de la table fait bloquer le document
    /// au mapping, indépendamment de l'origine). Un avoir groupé porte plusieurs <see cref="PivotDocumentRefDto"/>
    /// (F07-F08 §B.4).
    /// </summary>
    public static PivotDocumentDto BuildCreditNote(string sourceReference, string regimeCode, params PivotDocumentRefDto[] originRefs)
    {
        var line = new PivotLineDto(
            description: "Avoir — annulation adjudication lot 7",
            netAmount: 120.00m,
            quantity: 1m,
            unitPriceNet: 120.00m,
            sourceRegimeCodes: new[] { regimeCode },
            taxes: new[] { new PivotLineTaxDto(24.00m, 20m) },
            sourceLineRef: "ligne#1");

        return new PivotDocumentDto(
            sourceDocumentKind: "A",
            number: "A-2026-" + ((uint)sourceReference.GetHashCode(StringComparison.Ordinal)).ToString("D10", CultureInfo.InvariantCulture),
            issueDate: new DateTime(2026, 1, 20),
            sourceReference: sourceReference,
            supplier: new PivotPartyDto("Étude Fictïve SVV"),
            totals: new PivotTotalsDto(120.00m, 24.00m, 144.00m, 144.00m),
            operationCategory: OperationCategory.LivraisonBiens,
            customer: BusinessBuyer(),
            lines: new[] { line },
            creditNoteRefs: originRefs);
    }

    /// <summary>
    /// Construit une DÉCLARATION d'e-reporting B2C (flux 10.3, B2C01) — porte le marqueur
    /// <see cref="PivotDocumentDto.IsB2cReportingDeclaration"/> qui la route vers la capacité PA
    /// <c>SupportsB2cReporting</c> à l'envoi. Valeurs fictives (CLAUDE.md n°7).
    /// </summary>
    public static PivotDocumentDto BuildB2cReportingDeclaration(string sourceReference, string regimeCode)
    {
        var line = new PivotLineDto(
            description: "Déclaration 10.3 — adjudication lot 7",
            netAmount: 120.00m,
            quantity: 1m,
            unitPriceNet: 120.00m,
            sourceRegimeCodes: new[] { regimeCode },
            taxes: new[] { new PivotLineTaxDto(24.00m, 20m) },
            sourceLineRef: "ligne#1");

        return new PivotDocumentDto(
            sourceDocumentKind: "DECLARATION",
            number: "B2C-2026-" + ((uint)sourceReference.GetHashCode(StringComparison.Ordinal)).ToString("D10", CultureInfo.InvariantCulture),
            issueDate: new DateTime(2026, 1, 20),
            sourceReference: sourceReference,
            supplier: new PivotPartyDto("Étude Fictïve SVV"),
            totals: new PivotTotalsDto(120.00m, 24.00m, 144.00m, 144.00m),
            operationCategory: OperationCategory.LivraisonBiens,
            lines: new[] { line },
            isB2cReportingDeclaration: true);
    }

    /// <summary>
    /// Construit une DÉCLARATION de MARGE e-reporting B2C (flux 10.3, enchères — B4) telle que la SOURCE la
    /// produit, c.-à-d. SANS le marqueur 10.3 : l'agent ne pose JAMAIS <c>IsB2cReportingDeclaration</c> (il
    /// extrait des pièces, pas des déclarations — CLAUDE.md n°6) ; la PLATEFORME le DÉRIVE au read-time depuis
    /// le mapping validé (régime de la marge → E + VATEX-EU-J, B2cMarginMarking). L'adjudication ET l'honoraire
    /// ACHETEUR (porté en LIGNE rôle BuyerFee depuis BUG-17 volet b) portent le régime <paramref name="adjudicationRegimeCode"/>
    /// — mappé en part AUTRE au CHECK (marquage) ET en part FRAIS au B4 (taux). L'honoraire VENDEUR reste hors-lignes
    /// (<c>SellerFees</c>) et porte <paramref name="feeRegimeCode"/> (part FRAIS, B4). Aucune TVA distincte (art. 297 E :
    /// <c>Totals.TotalTax = 0</c>, adjudication exonérée). La marge n'est portée que par les honoraires
    /// (<c>marge = Σ honoraires</c>, F03 §2.4). Acheteur anonyme (B2C). Valeurs fictives (n°7).
    /// </summary>
    /// <param name="feeRegimeCode">
    /// Régime de l'honoraire VENDEUR (part FRAIS, B4). Doit résoudre le MÊME taux que l'honoraire acheteur
    /// (<paramref name="adjudicationRegimeCode"/> en part FRAIS) — sinon <c>B2cMarginResolver</c> bloque (MixedRates).
    /// Passer un régime non mappé en FRAIS (« REGIME_ABSENT ») exerce le fail-closed (UnmappedRate).
    /// </param>
    /// <param name="adjudicationRegimeCode">
    /// Régime de l'adjudication (part AUTRE), source du signal « régime de la marge ». Défaut « MARGE » =
    /// mappé E + VATEX-EU-J sur la table des harnais → la plateforme marque la déclaration. Passer un régime
    /// taxable (« NORMAL » → S) exerce la NON-dérivation (document non-marge, jamais routé vers B4).
    /// </param>
    /// <param name="customer">
    /// Acheteur. Défaut <c>null</c> = anonyme (B2C particulier). Passer un acheteur AVEC SIREN exerce le cas
    /// B2B (jamais marqué marge — l'e-invoicing B2B prime, F03 §2.4) et, au CHECK, la garde fail-closed
    /// « marge non classée » (honoraires sous régime exonéré + acheteur pro → bloqué).
    /// </param>
    public static PivotDocumentDto BuildB2cMarginDeclaration(
        string sourceReference,
        string feeRegimeCode,
        decimal sellerFeeTtc = 60.00m,
        decimal buyerFeeTtc = 60.00m,
        string lotReference = "lot-7",
        string adjudicationRegimeCode = "MARGE",
        PivotPartyDto? customer = null)
    {
        // Adjudication sous le régime de la marge : exonérée, AUCUNE TVA distincte (taux 0 / montant 0) — la marge
        // n'apparaît qu'au niveau de l'agrégat (art. 297 E, F03 §2.3).
        var adjudication = new PivotLineDto(
            description: "Adjudication lot (régime de la marge — exonérée)",
            netAmount: 120.00m,
            quantity: 1m,
            unitPriceNet: 120.00m,
            sourceRegimeCodes: new[] { adjudicationRegimeCode },
            taxes: new[] { new PivotLineTaxDto(0.00m, 0m) },
            sourceLineRef: "ligne#1");

        // HONORAIRE ACHETEUR porté en LIGNE (rôle BuyerFee, BUG-17 volet b) : il porte le MÊME régime que son
        // adjudication (parité production EncheresV6RowMapper.MapBaDocument) — consulté en part AUTRE au marquage
        // (toutes les lignes doivent être E+VATEX de marge) ET en part FRAIS pour le taux (job B4). NetAmount = TTC ;
        // taxe de ligne 0 (la TVA-marge n'apparaît pas, art. 297 E → TotalTax inchangé). La jambe VENDEUR reste
        // hors-lignes (SellerFees, décompte BV) et porte feeRegimeCode.
        var buyerFee = new PivotLineDto(
            description: "Honoraires acheteur lot",
            netAmount: buyerFeeTtc,
            quantity: 1m,
            unitPriceNet: buyerFeeTtc,
            sourceRegimeCodes: new[] { adjudicationRegimeCode },
            taxes: new[] { new PivotLineTaxDto(0.00m, 0m) },
            sourceLineRef: "ba#1",
            role: PivotLineRole.BuyerFee);

        // Total HT = adjudication (120) + honoraire acheteur EN LIGNE (TTC, art. 297 E → pas de TVA distincte) :
        // l'honoraire compte dans la somme des lignes (BR-CO-10) depuis qu'il est une ligne (BUG-17 volet b).
        // TotalTax reste 0 (marge propre, art. 297 E) → le marquage marge est préservé.
        decimal totalNet = 120.00m + buyerFeeTtc;
        return new PivotDocumentDto(
            sourceDocumentKind: "DECLARATION",
            number: "B2CM-2026-" + ((uint)sourceReference.GetHashCode(StringComparison.Ordinal)).ToString("D10", CultureInfo.InvariantCulture),
            issueDate: new DateTime(2026, 1, 20),
            sourceReference: sourceReference,
            supplier: new PivotPartyDto("Étude Fictïve SVV"),
            totals: new PivotTotalsDto(totalNet, 0.00m, totalNet, totalNet),
            operationCategory: OperationCategory.LivraisonBiens,
            customer: customer,
            lines: new[] { adjudication, buyerFee },
            sellerFees: new[] { new PivotSellerFeeDto(lotReference, sellerFeeTtc, feeRegimeCode, "bv#1") });
    }

    /// <summary>
    /// Construit un bordereau d'enchères TAXABLE (adjudication S 20 %, TVA distincte > 0) portant tout de même
    /// des honoraires — l'adaptateur EncheresV6 extrait les frais pour TOUT bordereau (marge ou taxable). C'est
    /// un document NON-marge RÉALISTE qui atteint <c>ReadyToSend</c> (TVA > 0 ⇒ hors de la garde « marge non
    /// classée ») et que le job B4 doit IGNORER (catégorie S ≠ E). Valeurs fictives (CLAUDE.md n°7).
    /// </summary>
    public static PivotDocumentDto BuildTaxableAuctionWithFees(string sourceReference, string regimeCode = "NORMAL")
    {
        var adjudication = new PivotLineDto(
            description: "Adjudication lot (taxable, S 20 %)",
            netAmount: 120.00m,
            quantity: 1m,
            unitPriceNet: 120.00m,
            sourceRegimeCodes: new[] { regimeCode },
            taxes: new[] { new PivotLineTaxDto(24.00m, 20m) },
            sourceLineRef: "ligne#1");

        // Honoraire acheteur EN LIGNE (rôle BuyerFee, BUG-17 volet b) : même régime que l'adjudication (« NORMAL »
        // → S en part AUTRE au marquage taxable ; taux 20 en part FRAIS au job taxable). NetAmount = TTC (60), taxe
        // de ligne 0 — le job recouvre le TTC = NetAmount + Σtaxe de ligne = 60.
        var buyerFee = new PivotLineDto(
            description: "Honoraires acheteur lot",
            netAmount: 60.00m,
            quantity: 1m,
            unitPriceNet: 60.00m,
            sourceRegimeCodes: new[] { regimeCode },
            taxes: new[] { new PivotLineTaxDto(0.00m, 0m) },
            sourceLineRef: "ba#1",
            role: PivotLineRole.BuyerFee);

        // HT = adjudication (120) + honoraire EN LIGNE (TTC 60) = 180 ; TVA = adjudication seule (24, l'honoraire
        // porte 0 de TVA de ligne) ; gross = 204. L'honoraire compte dans la somme des lignes (BR-CO-10, volet b).
        var totals = new PivotTotalsDto(180.00m, 24.00m, 204.00m, 204.00m);

        return new PivotDocumentDto(
            sourceDocumentKind: "F",
            number: "BAT-2026-" + ((uint)sourceReference.GetHashCode(StringComparison.Ordinal)).ToString("D10", CultureInfo.InvariantCulture),
            issueDate: new DateTime(2026, 1, 20),
            sourceReference: sourceReference,
            supplier: new PivotPartyDto("Étude Fictïve SVV"),
            totals: totals,
            operationCategory: OperationCategory.LivraisonBiens,
            lines: new[] { adjudication, buyerFee });
    }

    /// <summary>
    /// Construit un bordereau d'enchères d'EXONÉRÉ INTERNATIONAL (BUG-11) tel que la SOURCE le produit :
    /// l'adjudication porte la clé de régime par ZONE « EXP_{zone} » (l'agent dérive code_export + mode_livraison),
    /// mappée par la table validée du harnais → la plateforme DÉRIVE l'exonéré international (B2cExportMarking). Par
    /// défaut <c>EXP_HORSUE</c> (export hors UE 262 I → G → TLB1) ; passer <c>EXP_CEE</c> (intracom → K → TNT1) ou
    /// <c>EXP_FR</c> (franchise → G → TLB1). Adjudication détaxée (AUCUNE TVA distincte, <c>Totals.TotalTax = 0</c>),
    /// commission acheteur exonérée elle aussi (montant TTC = HT, le code source ne porte aucune TVA de frais sur un
    /// détaxé — F03 §2.8). Acheteur anonyme (B2C particulier). Valeurs fictives (CLAUDE.md n°7).
    /// </summary>
    public static PivotDocumentDto BuildExportAuctionWithFees(string sourceReference, string regimeCode = "EXP_HORSUE")
    {
        var adjudication = new PivotLineDto(
            description: "Adjudication lot (exonéré international — détaxé)",
            netAmount: 120.00m,
            quantity: 1m,
            unitPriceNet: 120.00m,
            sourceRegimeCodes: new[] { regimeCode },
            taxes: new[] { new PivotLineTaxDto(0.00m, 0m) },
            sourceLineRef: "ligne#1");

        // Honoraire acheteur EN LIGNE (rôle BuyerFee, BUG-17 volet b) : même régime que l'adjudication (« EXP_* »
        // → G/K détaxé en part AUTRE au marquage export). NetAmount = TTC (60), taxe de ligne 0, AUCUNE TVA de
        // frais source (commission détaxée, F03 §2.8 → SourceTaxAmount null) : base HT export = NetAmount − 0 = 60.
        var buyerFee = new PivotLineDto(
            description: "Honoraires acheteur lot",
            netAmount: 60.00m,
            quantity: 1m,
            unitPriceNet: 60.00m,
            sourceRegimeCodes: new[] { regimeCode },
            taxes: new[] { new PivotLineTaxDto(0.00m, 0m) },
            sourceLineRef: "ba#1",
            role: PivotLineRole.BuyerFee);

        // HT = adjudication (120) + honoraire EN LIGNE (TTC 60) = 180 ; détaxé → TVA 0, gross 180. L'honoraire
        // compte dans la somme des lignes (BR-CO-10, volet b).
        var totals = new PivotTotalsDto(180.00m, 0.00m, 180.00m, 180.00m);

        return new PivotDocumentDto(
            sourceDocumentKind: "F",
            number: "BAX-2026-" + ((uint)sourceReference.GetHashCode(StringComparison.Ordinal)).ToString("D10", CultureInfo.InvariantCulture),
            issueDate: new DateTime(2026, 1, 20),
            sourceReference: sourceReference,
            supplier: new PivotPartyDto("Étude Fictïve SVV"),
            totals: totals,
            operationCategory: OperationCategory.LivraisonBiens,
            lines: new[] { adjudication, buyerFee });
    }

    /// <summary>
    /// Construit un document B2C ORDINAIRE taxable (F03 §2.9, #7) — facture client ou note d'honoraires — tel que
    /// la SOURCE le produit : lignes taxables (« NORMAL » → S 20 %, TVA distincte), acheteur particulier (B2C),
    /// AUCUN frais d'enchères (discriminant « document ordinaire »). La <paramref name="operationCategory"/> porte
    /// la NATURE de l'opération, d'où le job dérive la TT-81 : <c>LivraisonBiens</c> → TLB1 (facture client),
    /// <c>PrestationServices</c> → TPS1 (note d'honoraires), <c>Mixte</c> → fail-closed. Valeurs fictives (CLAUDE.md n°7).
    /// </summary>
    public static PivotDocumentDto BuildPlainTaxableInvoice(
        string sourceReference,
        OperationCategory operationCategory,
        string regimeCode = "NORMAL")
    {
        var line = new PivotLineDto(
            description: "Ligne ordinaire taxable (S 20 %)",
            netAmount: 1000.00m,
            quantity: 1m,
            unitPriceNet: 1000.00m,
            sourceRegimeCodes: new[] { regimeCode },
            taxes: new[] { new PivotLineTaxDto(200.00m, 20m) },
            sourceLineRef: "ligne#1");

        return new PivotDocumentDto(
            sourceDocumentKind: "F",
            number: "FC-2026-" + ((uint)sourceReference.GetHashCode(StringComparison.Ordinal)).ToString("D10", CultureInfo.InvariantCulture),
            issueDate: new DateTime(2026, 1, 20),
            sourceReference: sourceReference,
            supplier: new PivotPartyDto("Étude Fictïve SVV"),
            totals: new PivotTotalsDto(1000.00m, 200.00m, 1200.00m, 1200.00m),
            operationCategory: operationCategory,
            lines: new[] { line });
    }

    public static IntegrationEvent<DocumentReceivedV1> Event(Guid documentId, string sourceReference, string payloadHash)
    {
        var payload = new DocumentReceivedV1
        {
            TenantId = PipelineCheckHarness.TenantSlug,
            DocumentId = documentId,
            SourceReference = sourceReference,
            PayloadHash = payloadHash,
            ReceivedAtUtc = DateTimeOffset.UtcNow,
        };

        return new IntegrationEvent<DocumentReceivedV1>
        {
            EventId = Guid.NewGuid(),
            EventType = "ingestion.document.received",
            OccurredAt = DateTimeOffset.UtcNow,
            CorrelationId = Guid.NewGuid(),
            ModuleSource = "Ingestion",
            Payload = payload,
            Version = 1,
        };
    }
}
