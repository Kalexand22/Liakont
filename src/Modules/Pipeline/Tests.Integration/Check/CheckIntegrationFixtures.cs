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
        return new PivotDocumentDto(
            sourceDocumentKind: "F",
            number: "F-2026-" + ((uint)sourceReference.GetHashCode(StringComparison.Ordinal)).ToString("D10", CultureInfo.InvariantCulture),
            issueDate: new DateTime(2026, 1, 10),
            sourceReference: sourceReference,
            supplier: new PivotPartyDto("Étude Fictïve SVV"),
            totals: new PivotTotalsDto(120.00m, 24.00m, 144.00m, 144.00m),
            operationCategory: operationCategory,
            customer: customer,
            lines: new[] { line });
    }

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
    /// le mapping validé (régime de la marge → E + VATEX-EU-J, B2cMarginMarking). L'adjudication porte un
    /// régime mappé en part AUTRE (CHECK), les honoraires un régime mappé en part FRAIS (B4) ; aucune TVA
    /// distincte (art. 297 E : <c>Totals.TotalTax = 0</c>, adjudication exonérée). La marge n'est portée que
    /// par les honoraires (<c>marge = Σ honoraires</c>, F03 §2.4). Acheteur anonyme (B2C). Valeurs fictives (n°7).
    /// </summary>
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
        // n'apparaît qu'au niveau de l'agrégat (art. 297 E, F03 §2.3). Les honoraires sont portés hors lignes.
        var adjudication = new PivotLineDto(
            description: "Adjudication lot (régime de la marge — exonérée)",
            netAmount: 120.00m,
            quantity: 1m,
            unitPriceNet: 120.00m,
            sourceRegimeCodes: new[] { adjudicationRegimeCode },
            taxes: new[] { new PivotLineTaxDto(0.00m, 0m) },
            sourceLineRef: "ligne#1");

        return new PivotDocumentDto(
            sourceDocumentKind: "DECLARATION",
            number: "B2CM-2026-" + ((uint)sourceReference.GetHashCode(StringComparison.Ordinal)).ToString("D10", CultureInfo.InvariantCulture),
            issueDate: new DateTime(2026, 1, 20),
            sourceReference: sourceReference,
            supplier: new PivotPartyDto("Étude Fictïve SVV"),
            totals: new PivotTotalsDto(120.00m, 0.00m, 120.00m, 120.00m),
            operationCategory: OperationCategory.LivraisonBiens,
            customer: customer,
            lines: new[] { adjudication },
            sellerFees: new[] { new PivotSellerFeeDto(lotReference, sellerFeeTtc, feeRegimeCode, "bv#1") },
            buyerFees: new[] { new PivotBuyerFeeDto(lotReference, buyerFeeTtc, feeRegimeCode, "ba#1") });
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

        return new PivotDocumentDto(
            sourceDocumentKind: "F",
            number: "BAT-2026-" + ((uint)sourceReference.GetHashCode(StringComparison.Ordinal)).ToString("D10", CultureInfo.InvariantCulture),
            issueDate: new DateTime(2026, 1, 20),
            sourceReference: sourceReference,
            supplier: new PivotPartyDto("Étude Fictïve SVV"),
            totals: new PivotTotalsDto(120.00m, 24.00m, 144.00m, 144.00m),
            operationCategory: OperationCategory.LivraisonBiens,
            lines: new[] { adjudication },
            buyerFees: new[] { new PivotBuyerFeeDto("lot-7", 60.00m, regimeCode, "ba#1") });
    }

    /// <summary>
    /// Construit un bordereau d'enchères d'EXPORT HORS UE (BUG-11) tel que la SOURCE le produit : l'adjudication
    /// porte la clé de régime COMPOSITE « {régime}_EXP_HORSUE » (l'agent combine code_export + mode_livraison),
    /// mappée G + VATEX-EU-G (détaxé, art. 262 I) par la table validée du harnais → la plateforme DÉRIVE l'export
    /// (B2cExportMarking). Adjudication détaxée (AUCUNE TVA distincte, <c>Totals.TotalTax = 0</c>), commission
    /// acheteur exonérée elle aussi (montant TTC = HT, le code source ne porte aucune TVA de frais sur un export —
    /// F03 §2.8). Acheteur anonyme (B2C particulier). Valeurs fictives (CLAUDE.md n°7).
    /// </summary>
    public static PivotDocumentDto BuildExportAuctionWithFees(string sourceReference, string regimeCode = "EXPORT_HORSUE")
    {
        var adjudication = new PivotLineDto(
            description: "Adjudication lot (export hors UE — détaxé, art. 262 I)",
            netAmount: 120.00m,
            quantity: 1m,
            unitPriceNet: 120.00m,
            sourceRegimeCodes: new[] { regimeCode },
            taxes: new[] { new PivotLineTaxDto(0.00m, 0m) },
            sourceLineRef: "ligne#1");

        return new PivotDocumentDto(
            sourceDocumentKind: "F",
            number: "BAX-2026-" + ((uint)sourceReference.GetHashCode(StringComparison.Ordinal)).ToString("D10", CultureInfo.InvariantCulture),
            issueDate: new DateTime(2026, 1, 20),
            sourceReference: sourceReference,
            supplier: new PivotPartyDto("Étude Fictïve SVV"),
            totals: new PivotTotalsDto(120.00m, 0.00m, 120.00m, 120.00m),
            operationCategory: OperationCategory.LivraisonBiens,
            lines: new[] { adjudication },
            buyerFees: new[] { new PivotBuyerFeeDto("lot-7", 60.00m, regimeCode, "ba#1") });
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
