namespace Liakont.Agent.Contracts.Pivot;

using System;
using System.Collections.Generic;

/// <summary>
/// Le document à transmettre — modèle pivot aligné EN 16931 (F01-F02 §3.1). DTO PUR : aucun
/// calcul, aucune validation, aucune règle fiscale ; il porte les montants calculés par la source
/// et laisse <c>null</c> tout champ absent (F01-F02 §3.7). Tous les montants sont en
/// <see cref="decimal"/> (CLAUDE.md n°1). La classification facture/avoir et le mapping TVA
/// vivent sur la PLATEFORME, jamais ici (ADR-0004 D3-3).
/// </summary>
public sealed class PivotDocumentDto
{
    /// <summary>Crée un document pivot.</summary>
    /// <param name="sourceDocumentKind">
    /// Valeur de type de document de la source, BRUTE (ADR-0004 D3-3) : l'adaptateur ne classe PAS
    /// facture/avoir (souvent un signe ou un champ ambigu) — la classification vit dans Validation.
    /// </param>
    /// <param name="number">Numéro du document (EN 16931 BT-1) — clé d'idempotence vers la PA.</param>
    /// <param name="issueDate">Date d'émission (EN 16931 BT-2).</param>
    /// <param name="sourceReference">Identifiant du document dans le système source (réconciliation + audit).</param>
    /// <param name="supplier">
    /// Le vendeur / fournisseur — l'émetteur au sens EN 16931 BG-4 (la SVV / l'entreprise cliente).
    /// <c>null</c> quand l'agent ne le porte pas : l'émetteur est l'identité du TENANT, REMPLIE par la
    /// plateforme à l'ingestion depuis le profil tenant (ADR-0031 amendé) — l'agent n'extrait que la base
    /// source et ne devine jamais le SIREN (CLAUDE.md n°2).
    /// </param>
    /// <param name="totals">Totaux de contrôle (EN 16931 BG-22).</param>
    /// <param name="operationCategory">
    /// Nature de l'opération (mention obligatoire réforme). <c>null</c> côté agent : remplie par la
    /// plateforme à l'ingestion depuis le paramétrage fiscal du tenant (ADR-0031 amendé).
    /// </param>
    /// <param name="currencyCode">Devise ISO 4217 (EN 16931 BT-5). Défaut « EUR ».</param>
    /// <param name="customer">Le destinataire (EN 16931 BG-7) — nul en B2C sans tiers identifié.</param>
    /// <param name="lines">Lignes du document (EN 16931 BG-25).</param>
    /// <param name="creditNoteRefs">
    /// Références des documents d'origine pour un avoir — multi-références (avoirs groupés), chacune
    /// avec sa date obligatoire (acceptance PIV01 ; jamais une liste de chaînes).
    /// </param>
    /// <param name="payments">Encaissements bruts rattachés (F09).</param>
    /// <param name="documentCharges">Charges/remises de niveau document (EN 16931 BG-20 / BG-21).</param>
    /// <param name="invoicer">
    /// L'émetteur de la facture quand il diffère du vendeur (auto-facturation / facturation pour
    /// compte de tiers — ADR-0004 D3-6). Nul = identique au <paramref name="supplier"/>.
    /// </param>
    /// <param name="payee">Bénéficiaire du paiement quand il diffère du vendeur (affacturage, EN 16931 BG-10).</param>
    /// <param name="isSelfBilled">Indicateur BRUT d'auto-facturation porté par la source (ADR-0004 D3-6).</param>
    /// <param name="prepaidAmount">Montant déjà payé / acompte (EN 16931 BT-113), pour le chaînage acompte→solde.</param>
    /// <param name="sourceData">Données source brutes utiles à la traçabilité (JSON), montants originaux non arrondis.</param>
    /// <param name="paymentDueDate">
    /// Date d'échéance de paiement (EN 16931 BT-9), donnée CONTRACTUELLE de la source — JAMAIS inventée ni
    /// défaultée (CLAUDE.md n°2) : un maillon qui ne la connaît pas la laisse <c>null</c>. Paramètre ADDITIF
    /// en fin de constructeur (ADR-0007) : un appelant existant reste valide, et le hash canonique d'un
    /// document sans échéance est INCHANGÉ (champ optionnel omis — F01-F02 §3.1, F14 §3.2/O11).
    /// </param>
    /// <param name="isB2cReportingDeclaration">
    /// Marqueur de FLUX : ce document est une DÉCLARATION d'e-reporting B2C (flux 10.3, F09 §10.3 / B2C01) — il
    /// est routé vers la capacité PA <c>SupportsB2cReporting</c> à l'envoi, jamais via une règle de détection
    /// (BuyerLooksProfessionalRule). Marqueur de PLATEFORME (l'agent ne le porte jamais : il extrait des
    /// pièces source, pas des déclarations) : <c>false</c> par défaut. Paramètre ADDITIF en fin de constructeur
    /// (pattern EXT01, ADR-0007) émis SEULEMENT quand il est vrai : le hash canonique d'un document qui n'est
    /// PAS une déclaration 10.3 est INCHANGÉ (octet par octet).
    /// </param>
    /// <param name="sellerFees">
    /// Frais vendeur (BV) au grain lot, DONNÉE DE CALCUL de la marge e-reporting B2C (B2C-08) — JAMAIS des
    /// lignes facturées : porté HORS de <paramref name="lines"/>, sans aucune TVA distincte (CGI art. 297 E),
    /// et n'impacte pas <paramref name="totals"/>. Alimente le calcul de B2C-09b, pas le document transmis.
    /// Paramètre ADDITIF en fin de constructeur (pattern EXT01, ADR-0007) : <c>null</c> par défaut ; une liste
    /// VIDE est traitée comme ABSENTE (normalisée en <c>null</c> — l'inverse de <paramref name="lines"/> qui
    /// coalesce <c>null</c> en collection vide). Émis dans le JSON canonique SEULEMENT quand il est porté
    /// (non-null et non vide), pour que le hash d'un document sans frais vendeur reste INCHANGÉ (octet par
    /// octet — seul un champ ABSENT, ou vide, est hash-neutre).
    /// </param>
    /// <param name="buyerFees">
    /// Frais ACHETEUR au grain lot, 2e jambe de la marge e-reporting B2C (B2C-08c) — symétrique strict de
    /// <paramref name="sellerFees"/> : DONNÉE DE CALCUL de la marge (<c>marge = Σ frais acheteur + Σ frais
    /// vendeur</c>, F03 §2.4), JAMAIS des lignes facturées, sans aucune TVA distincte (CGI art. 297 E), et
    /// n'impacte pas <paramref name="totals"/>. Alimente le calcul de B2C-09b, pas le document transmis.
    /// Paramètre ADDITIF en fin de constructeur (pattern EXT01, ADR-0007) : <c>null</c> par défaut ; une liste
    /// VIDE est traitée comme ABSENTE (normalisée en <c>null</c>). Émis dans le JSON canonique SEULEMENT quand
    /// il est porté (non-null et non vide), pour que le hash d'un document sans frais acheteur reste INCHANGÉ.
    /// </param>
    /// <param name="invoicePeriod">
    /// Période de facturation (EN 16931 BG-14 : BT-73/BT-74), slot RÉSERVÉ pour les flux d'abonnement /
    /// usage (ADR-0004 D4 Famille 3 / §5, RD406). Paramètre ADDITIF en fin de constructeur (ADR-0007) :
    /// <c>null</c> par défaut, OMIS du JSON canonique tant qu'il est absent → hash INCHANGÉ. Inerte en V1
    /// (aucun sérialiseur PA ne le projette) ; porté tel quel par la source, jamais inventé (CLAUDE.md n°2).
    /// </param>
    /// <param name="paymentTerms">
    /// Termes / conditions de paiement (EN 16931 BT-20). Donnée de l'ENTREPRISE (CGV), jamais inventée
    /// (CLAUDE.md n°2) : défaut TENANT « Mentions de facturation » (F12-A §3.4) injecté au read-time,
    /// surchargeable par document. Satisfait BR-CO-25 pour un montant dû positif (alternative à BT-9).
    /// Paramètre ADDITIF en fin de constructeur (ADR-0007) : <c>null</c> par défaut, OMIS du JSON canonique
    /// quand absent → hash INCHANGÉ. Voir F16 §3.5.
    /// </param>
    /// <param name="notes">
    /// Notes de niveau document (EN 16931 BG-1) — porte les mentions légales FR obligatoires (BR-FR-05 :
    /// PMD pénalités de retard, PMT indemnité de recouvrement, AAB escompte), contenu = paramètre TENANT
    /// (F12-A §3.4), surchargeable par document. Paramètre ADDITIF en fin de constructeur (ADR-0007) :
    /// <c>null</c> par défaut ; une liste VIDE est traitée comme ABSENTE (normalisée en <c>null</c>, comme
    /// <paramref name="sellerFees"/>) ; émis dans le JSON canonique SEULEMENT quand porté → hash INCHANGÉ.
    /// </param>
    /// <param name="deliveryDate">
    /// Date de livraison effective (EN 16931 BT-72) — aux enchères, la livraison du lot intervient à
    /// l'adjudication (= date de vente, fait du modèle, pas une fabrication). Rend l'élément livraison CII
    /// NON vide (PEPPOL-EN16931-R008). Paramètre ADDITIF en fin de constructeur (ADR-0007) : <c>null</c> par
    /// défaut (l'émetteur applique alors la date d'émission), OMIS du JSON canonique quand absent → hash
    /// INCHANGÉ. Voir F16 §3.5.
    /// </param>
    public PivotDocumentDto(
        string sourceDocumentKind,
        string number,
        DateTime issueDate,
        string sourceReference,
        PivotPartyDto? supplier,
        PivotTotalsDto totals,
        OperationCategory? operationCategory,
        string currencyCode = "EUR",
        PivotPartyDto? customer = null,
        IReadOnlyList<PivotLineDto>? lines = null,
        IReadOnlyList<PivotDocumentRefDto>? creditNoteRefs = null,
        IReadOnlyList<PivotPaymentDto>? payments = null,
        IReadOnlyList<PivotDocumentChargeDto>? documentCharges = null,
        PivotPartyDto? invoicer = null,
        PivotPartyDto? payee = null,
        bool isSelfBilled = false,
        decimal? prepaidAmount = null,
        string? sourceData = null,
        DateTime? paymentDueDate = null,
        bool isB2cReportingDeclaration = false,
        IReadOnlyList<PivotSellerFeeDto>? sellerFees = null,
        IReadOnlyList<PivotBuyerFeeDto>? buyerFees = null,
        PivotInvoicePeriodDto? invoicePeriod = null,
        string? paymentTerms = null,
        IReadOnlyList<PivotDocumentNoteDto>? notes = null,
        DateTime? deliveryDate = null)
    {
        SourceDocumentKind = sourceDocumentKind;
        Number = number;
        IssueDate = issueDate;
        SourceReference = sourceReference;
        Supplier = supplier;
        Totals = totals;
        OperationCategory = operationCategory;
        CurrencyCode = currencyCode;
        Customer = customer;
        Lines = lines ?? Array.Empty<PivotLineDto>();
        CreditNoteRefs = creditNoteRefs ?? Array.Empty<PivotDocumentRefDto>();
        Payments = payments ?? Array.Empty<PivotPaymentDto>();
        DocumentCharges = documentCharges ?? Array.Empty<PivotDocumentChargeDto>();
        Invoicer = invoicer;
        Payee = payee;
        IsSelfBilled = isSelfBilled;
        PrepaidAmount = prepaidAmount;
        SourceData = sourceData;
        PaymentDueDate = paymentDueDate;
        IsB2cReportingDeclaration = isB2cReportingDeclaration;
        // Vide ≡ absent : une liste non-null mais VIDE est normalisée en null pour rester hash-neutre
        // (un frais vendeur vide ne porte aucune information ; seul un champ OMIS l'est — pattern EXT01).
        SellerFees = sellerFees != null && sellerFees.Count > 0 ? sellerFees : null;
        // Symétrique du frais vendeur (B2C-08c) : vide ≡ absent → normalisé en null pour rester hash-neutre.
        BuyerFees = buyerFees != null && buyerFees.Count > 0 ? buyerFees : null;
        InvoicePeriod = invoicePeriod;
        PaymentTerms = paymentTerms;
        // Vide ≡ absent : une liste de notes non-null mais VIDE est normalisée en null pour rester hash-neutre
        // (mêmes règles que SellerFees/BuyerFees — seul un champ OMIS, ou vide, l'est ; pattern EXT01).
        Notes = notes != null && notes.Count > 0 ? notes : null;
        DeliveryDate = deliveryDate;
    }

    /// <summary>Type de document de la source, BRUT (ADR-0004 D3-3).</summary>
    public string SourceDocumentKind { get; }

    /// <summary>Numéro du document (EN 16931 BT-1).</summary>
    public string Number { get; }

    /// <summary>Date d'émission (EN 16931 BT-2).</summary>
    public DateTime IssueDate { get; }

    /// <summary>Identifiant du document dans le système source.</summary>
    public string SourceReference { get; }

    /// <summary>
    /// Le vendeur / fournisseur (EN 16931 BG-4). <c>null</c> tant que la plateforme ne l'a pas rempli
    /// depuis le profil tenant à l'ingestion (ADR-0031 amendé).
    /// </summary>
    public PivotPartyDto? Supplier { get; }

    /// <summary>Totaux de contrôle (EN 16931 BG-22).</summary>
    public PivotTotalsDto Totals { get; }

    /// <summary>
    /// Nature de l'opération (mention obligatoire réforme). <c>null</c> tant que la plateforme ne l'a
    /// pas remplie depuis le paramétrage fiscal du tenant à l'ingestion (ADR-0031 amendé).
    /// </summary>
    public OperationCategory? OperationCategory { get; }

    /// <summary>Devise ISO 4217 (EN 16931 BT-5).</summary>
    public string CurrencyCode { get; }

    /// <summary>Le destinataire (EN 16931 BG-7).</summary>
    public PivotPartyDto? Customer { get; }

    /// <summary>Lignes du document (EN 16931 BG-25).</summary>
    public IReadOnlyList<PivotLineDto> Lines { get; }

    /// <summary>Références des documents d'origine d'un avoir (multi-références, date obligatoire).</summary>
    public IReadOnlyList<PivotDocumentRefDto> CreditNoteRefs { get; }

    /// <summary>Encaissements bruts rattachés (F09).</summary>
    public IReadOnlyList<PivotPaymentDto> Payments { get; }

    /// <summary>Charges/remises de niveau document (EN 16931 BG-20 / BG-21).</summary>
    public IReadOnlyList<PivotDocumentChargeDto> DocumentCharges { get; }

    /// <summary>Émetteur de la facture s'il diffère du vendeur (auto-facturation — ADR-0004 D3-6).</summary>
    public PivotPartyDto? Invoicer { get; }

    /// <summary>Bénéficiaire du paiement s'il diffère du vendeur (affacturage, EN 16931 BG-10).</summary>
    public PivotPartyDto? Payee { get; }

    /// <summary>Indicateur BRUT d'auto-facturation porté par la source (ADR-0004 D3-6).</summary>
    public bool IsSelfBilled { get; }

    /// <summary>Montant déjà payé / acompte (EN 16931 BT-113).</summary>
    public decimal? PrepaidAmount { get; }

    /// <summary>Données source brutes utiles à la traçabilité (JSON).</summary>
    public string? SourceData { get; }

    /// <summary>
    /// Date d'échéance de paiement (EN 16931 BT-9), <c>null</c> si la source ne la porte pas — jamais
    /// défaultée (CLAUDE.md n°2). Levée la limitation BR-CO-25 pour les factures à montant dû positif
    /// (F14 §3.2/O11) : émise vers la PA seulement quand elle est présente.
    /// </summary>
    public DateTime? PaymentDueDate { get; }

    /// <summary>
    /// Vrai si ce document est une DÉCLARATION d'e-reporting B2C (flux 10.3, F09 / B2C01) — routée vers la
    /// capacité PA <c>SupportsB2cReporting</c> à l'envoi. <c>false</c> pour tout autre document (facture,
    /// avoir, B2B) : marqueur de plateforme, jamais porté par l'agent. Émis dans le JSON canonique SEULEMENT
    /// quand il est vrai (champ additif hash-neutre — pattern EXT01).
    /// </summary>
    public bool IsB2cReportingDeclaration { get; }

    /// <summary>
    /// Frais vendeur (BV) au grain lot — DONNÉE DE CALCUL de la marge e-reporting B2C (B2C-08), alimente
    /// B2C-09b. <c>null</c> pour tout document qui ne porte pas de frais vendeur (une liste vide est normalisée en
    /// <c>null</c> au constructeur) : émis dans le JSON canonique SEULEMENT quand il est porté (champ additif
    /// hash-neutre — pattern EXT01). N'est JAMAIS une ligne taxable et n'impacte ni <see cref="Lines"/> ni
    /// <see cref="Totals"/> ; aucune TVA distincte (CGI art. 297 E).
    /// </summary>
    public IReadOnlyList<PivotSellerFeeDto>? SellerFees { get; }

    /// <summary>
    /// Frais ACHETEUR au grain lot — 2e jambe de la marge e-reporting B2C (B2C-08c), symétrique strict de
    /// <see cref="SellerFees"/>, alimente B2C-09b (<c>marge = Σ frais acheteur + Σ frais vendeur</c>).
    /// <c>null</c> pour tout document qui ne porte pas de frais acheteur (une liste vide est normalisée en
    /// <c>null</c> au constructeur) : émis dans le JSON canonique SEULEMENT quand il est porté (champ additif
    /// hash-neutre — pattern EXT01). N'est JAMAIS une ligne taxable et n'impacte ni <see cref="Lines"/> ni
    /// <see cref="Totals"/> ; aucune TVA distincte (CGI art. 297 E).
    /// </summary>
    public IReadOnlyList<PivotBuyerFeeDto>? BuyerFees { get; }

    /// <summary>
    /// Période de facturation (EN 16931 BG-14 : BT-73/BT-74) — slot RÉSERVÉ pour les flux d'abonnement /
    /// usage (ADR-0004 D4 Famille 3 / §5, RD406). <c>null</c> tant que la source ne la porte pas : champ
    /// optionnel OMIS du JSON canonique → hash INCHANGÉ. Inerte en V1 (aucun sérialiseur PA ne le projette).
    /// </summary>
    public PivotInvoicePeriodDto? InvoicePeriod { get; }

    /// <summary>
    /// Termes / conditions de paiement (EN 16931 BT-20) — donnée de l'entreprise (CGV), défaut TENANT
    /// (F12-A §3.4) surchargeable par document, jamais inventée. Satisfait BR-CO-25 pour un montant dû
    /// positif (alternative à <see cref="PaymentDueDate"/>). <c>null</c> ⇒ OMIS du JSON canonique → hash
    /// INCHANGÉ (champ additif hash-neutre — pattern EXT01). Voir F16 §3.5.
    /// </summary>
    public string? PaymentTerms { get; }

    /// <summary>
    /// Notes de niveau document (EN 16931 BG-1) — porte les mentions légales FR obligatoires (BR-FR-05 :
    /// PMD/PMT/AAB), contenu = paramètre TENANT (F12-A §3.4) surchargeable par document. <c>null</c> pour un
    /// document sans note (une liste vide est normalisée en <c>null</c> au constructeur) : émis dans le JSON
    /// canonique SEULEMENT quand porté → hash INCHANGÉ (champ additif hash-neutre — pattern EXT01).
    /// </summary>
    public IReadOnlyList<PivotDocumentNoteDto>? Notes { get; }

    /// <summary>
    /// Date de livraison effective (EN 16931 BT-72) — aux enchères, livraison à l'adjudication (= date de
    /// vente). Rend l'élément livraison CII non vide (PEPPOL-EN16931-R008). <c>null</c> ⇒ OMIS du JSON
    /// canonique → hash INCHANGÉ (l'émetteur applique alors la date d'émission). Voir F16 §3.5.
    /// </summary>
    public DateTime? DeliveryDate { get; }
}
