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
    /// <param name="invoicePeriod">
    /// Période de facturation (EN 16931 BG-14 : BT-73/BT-74), slot RÉSERVÉ pour les flux d'abonnement /
    /// usage (ADR-0004 D4 Famille 3 / §5, RD406). Paramètre ADDITIF en fin de constructeur (ADR-0007) :
    /// <c>null</c> par défaut, OMIS du JSON canonique tant qu'il est absent → hash INCHANGÉ. Inerte en V1
    /// (aucun sérialiseur PA ne le projette) ; porté tel quel par la source, jamais inventé (CLAUDE.md n°2).
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
        PivotInvoicePeriodDto? invoicePeriod = null)
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
        InvoicePeriod = invoicePeriod;
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
    /// Période de facturation (EN 16931 BG-14 : BT-73/BT-74) — slot RÉSERVÉ pour les flux d'abonnement /
    /// usage (ADR-0004 D4 Famille 3 / §5, RD406). <c>null</c> tant que la source ne la porte pas : champ
    /// optionnel OMIS du JSON canonique → hash INCHANGÉ. Inerte en V1 (aucun sérialiseur PA ne le projette).
    /// </summary>
    public PivotInvoicePeriodDto? InvoicePeriod { get; }
}
