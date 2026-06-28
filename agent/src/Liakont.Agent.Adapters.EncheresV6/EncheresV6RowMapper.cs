namespace Liakont.Agent.Adapters.EncheresV6;

using System;
using System.Collections.Generic;
using System.Globalization;
using Liakont.Agent.Adapters.EncheresV6.Source;
using Liakont.Agent.Contracts;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Core.Extraction;
using Newtonsoft.Json;

/// <summary>
/// Transforme les bordereaux BRUTS d'EncheresV6 en documents pivot EN 16931 — transformation PARTAGÉE
/// entre l'extracteur ODBC réel et le mode fixtures (seule la source des lignes change). Respecte
/// strictement le contrat d'extraction (F01-F02 §4.2) :
/// <list type="bullet">
///   <item>il ne mappe PAS la TVA (R3) : <c>SourceRegimeCodes</c> bruts, <c>CategoryCode</c>/<c>VatexCode</c> nuls ;</item>
///   <item>il ne classe PAS facture/avoir : <c>SourceDocumentKind</c> brut (ADR-0004 D3-3) ;</item>
///   <item>il ne porte NI l'émetteur NI la nature d'opération (<c>supplier</c>/<c>operationCategory</c> = <c>null</c>) :
///   la plateforme les remplit à l'ingestion depuis le profil tenant (ADR-0031 amendé, parité DemoErpA) ;</item>
///   <item>la seule arithmétique est la conversion des flottants legacy en <c>decimal</c> half-up au centime
///   (CLAUDE.md n°1), l'original étant conservé dans <c>SourceData</c>.</item>
/// </list>
/// MODÈLE DE MARGE (validé + sourcé) : le bordereau ACHETEUR (BA) porte la commission acheteur en
/// <see cref="PivotDocumentDto.BuyerFees"/> (lignes type 1, TTC) ; le bordereau VENDEUR (BV) porte la
/// commission vendeur en <see cref="PivotDocumentDto.SellerFees"/> (lignes type 2, TTC). La plateforme
/// agrège les deux jambes en un seul report SE (F03 §2.4/§2.5). Les débours (BA type 2, BV type 3) sont
/// HORS marge : non portés en frais. L'adjudication est la seule LIGNE du document (sous régime de la
/// marge sa TVA est nulle → document marge propre, art. 297 E).
/// </summary>
internal static class EncheresV6RowMapper
{
    /// <summary>Type de pièce source « bordereau de vente ».</summary>
    internal const string PieceVente = EncheresV6Schema.PieceVente;

    /// <summary>Type de pièce source « avoir ».</summary>
    internal const string PieceAvoir = EncheresV6Schema.PieceAvoir;

    /// <summary>Préfixe namespacé de la référence source d'un bordereau acheteur (anti-collision cross-agent).</summary>
    internal const string SourceRefBaPrefix = "encheresv6:ba:";

    /// <summary>Préfixe namespacé de la référence source d'un bordereau vendeur.</summary>
    internal const string SourceRefBvPrefix = "encheresv6:bv:";

    /// <summary>Préfixe namespacé de la référence source d'une facture client (document ordinaire hors enchères).</summary>
    internal const string SourceRefFcPrefix = "encheresv6:fc:";

    /// <summary>Préfixe namespacé de la référence source d'une note d'honoraires d'inventaire.</summary>
    internal const string SourceRefNhPrefix = "encheresv6:nh:";

    private const string DeviseDomestique = "EUR";

    /// <summary>
    /// Table de correspondance des codes pays NON-ISO connus de la source EncheresV6 (<c>code_pays</c>) vers
    /// ISO 3166-1 alpha-2. EXTENSIBLE (BUG-18) : on AJOUTE une entrée par code legacy constaté, jamais un nouveau
    /// <c>case</c> codé en dur. Couvre les nations du Royaume-Uni (<c>ENG</c>/<c>SCO</c>/<c>WAL</c>/<c>NIR</c> =
    /// subdivisions ISO 3166-2, pas des pays alpha-2 → <c>GB</c>, BUG-9) et le Japon (<c>JAP</c> → <c>JP</c>,
    /// BUG-18, doc n°2000020). Clés normalisées en MAJUSCULES (la recherche applique <c>Trim().ToUpperInvariant()</c>).
    /// Normalisation de DONNÉE legacy (miroir du nettoyage devise « EURO » → « EUR »), jamais une règle fiscale
    /// (aucune catégorie TVA / VATEX / seuil inventé — CLAUDE.md n°2).
    /// </summary>
    private static readonly Dictionary<string, string> NonIsoCountryCodeMap = new(StringComparer.Ordinal)
    {
        ["ENG"] = "GB",
        ["SCO"] = "GB",
        ["WAL"] = "GB",
        ["NIR"] = "GB",
        ["JAP"] = "JP",
    };

    /// <summary>
    /// Mappe un bordereau ACHETEUR (vente ou avoir) en document pivot — jambe ACHETEUR de la marge.
    /// Lines = adjudication des lots (type 1) ; BuyerFees = commission acheteur (type 1, TTC). Pour un avoir,
    /// <paramref name="creditNoteOrigin"/> DOIT être l'entête d'origine résolue (sinon blocage, jamais deviné).
    /// </summary>
    /// <param name="bordereau">Le bordereau acheteur source (avec ses lignes type 1).</param>
    /// <param name="creditNoteOrigin">Bordereau d'origine d'un avoir, sinon <c>null</c>.</param>
    /// <returns>Le document pivot correspondant.</returns>
    public static PivotDocumentDto MapBaDocument(EncheresV6Bordereau bordereau, EncheresV6Bordereau? creditNoteOrigin)
    {
        if (bordereau is null)
        {
            throw new ArgumentNullException(nameof(bordereau));
        }

        string kind = RequireField(bordereau.BordereauOuAvoir, "bordereau_ou_avoir", bordereau.NoBa);
        string noBa = RequireField(bordereau.NoBa, "no_ba", bordereau.NoBa);
        RequireDate(bordereau.DateVente, "date_vente", noBa);

        var lines = new List<PivotLineDto>();
        decimal totalNet = 0m;
        decimal totalTax = 0m;

        // Jeton de ZONE d'export (F03 §2.8) : dérive du flag source code_export + le mode de livraison une clé de
        // régime par ZONE (RegimeKeyShape.Composite, « EXP_HORSUE »/« EXP_CEE »/« EXP_FR ») — l'exonération
        // internationale prime sur le régime domestique, donc la zone SUFFIT à classer (la plateforme mappe une
        // règle par zone). Transport de donnée source (export + zone ; le régime brut reste dans SourceData),
        // AUCUNE dérivation fiscale ici : la catégorie/VATEX restent décidées par la table validée (CLAUDE.md n°6).
        string? exportZone = ExportZone(bordereau);

        foreach (EncheresV6Ligne ligne in bordereau.Lignes)
        {
            // L'extracteur ne charge que les lignes de LOT (type 1) sur le BA ; on reste défensif.
            if (!IsLotLineBa(ligne.TypeLigne))
            {
                continue;
            }

            decimal adjNet = RoundAmount(ligne.MontantAdjHt);

            // Arrondi PAR COMPOSANTE puis somme (CLAUDE.md n°1) : chaque montant source (TVA incluse / en sus)
            // est un montant au centime ; Round(a)+Round(b) évite le décalage d'un centime de Round(a+b) sur un
            // flottant legacy bruité. En régime de la marge l'un des deux est nul (adjudication exonérée).
            decimal adjTax = RoundAmount(ligne.MttTvaInclusAdj) + RoundAmount(ligne.MttTvaEnPlusAdj);

            // MÊME clé régime/zone pour l'adjudication ET l'honoraire acheteur de ce lot (la zone d'export prime
            // pareillement sur les deux composantes — F03 §2.8). Calculée une fois, partagée par les deux lignes.
            IReadOnlyList<string> regimeCodes = RegimeCodes(ComposeRegimeKey(ligne.CodeRegime, exportZone));

            totalNet += adjNet;
            totalTax += adjTax;

            lines.Add(new PivotLineDto(
                description: LineDescription(ligne.Designation, "Adjudication", ligne.NoLignePv),
                netAmount: adjNet,
                quantity: 1m,
                unitPriceNet: null,
                sourceRegimeCodes: regimeCodes,
                taxes: new[] { new PivotLineTaxDto(taxAmount: adjTax, rate: null, categoryCode: null, vatexCode: null) },
                sourceLineRef: ligne.NoLignePv,
                sourceData: BuildBaLineSourceData(ligne)));

            // HONORAIRE ACHETEUR porté en LIGNE (rôle BuyerFee) — F03 §2.3 amendement (2026-06-26, BUG-17 volet b).
            // Au lieu d'un side-channel hors-lignes (BuyerFees), la commission acheteur est une VRAIE 2e ligne du
            // bordereau : le total de la pièce devient réel (adjudication + honoraire) et la facture électronique la
            // porte (le sérialiseur ne lit que les lignes). NetAmount = TTC (HT + TVA, arrondi par composante puis
            // somme, CLAUDE.md n°1) ; taxe de ligne à 0 — l'agent ne CLASSE pas (CLAUDE.md n°6), la catégorie vient du
            // mapping plateforme (E+VATEX sous marge → TVA « dans la marge », non apparente, art. 297 E ; S sous prix
            // total). La ligne reste TTC/taxe 0 (le dé-pliage HT/TVA d'un honoraire taxable est DÉFÉRÉ, BUG-17). La TVA
            // de frais BRUTE est transportée à part (SourceTaxAmount) pour que la plateforme recouvre la base HT d'un
            // export (F03 §2.8, recouvrement EFFECTIF) et, à terme, dé-plie la TVA au régime du prix total (F03 §2.7) ;
            // omise quand elle vaut 0 (commission détaxée → hash inchangé). Le total de TVA n'inclut PAS la TVA-marge
            // (297 E, TotalTax == 0 préservé) ; elle est calculée par le job B4 (mapping part Frais).
            decimal fraisTvaTtc = RoundAmount(ligne.MontantTvaFrais);
            decimal honoraireTtc = RoundAmount(ligne.MontantFraisHt) + fraisTvaTtc;
            totalNet += honoraireTtc;

            lines.Add(new PivotLineDto(
                description: HonoraireDescription(ligne.NoLignePv),
                netAmount: honoraireTtc,
                quantity: 1m,
                unitPriceNet: null,
                sourceRegimeCodes: regimeCodes,
                taxes: new[] { new PivotLineTaxDto(taxAmount: 0m, rate: null, categoryCode: null, vatexCode: null) },
                sourceLineRef: ligne.NoLignePv,
                sourceData: BuildBaLineSourceData(ligne),
                role: PivotLineRole.BuyerFee,
                sourceTaxAmount: fraisTvaTtc == 0m ? null : fraisTvaTtc));
        }

        PivotDocumentRefDto[] creditNoteRefs = MapBaCreditNoteRefs(bordereau, kind, creditNoteOrigin);

        return new PivotDocumentDto(
            sourceDocumentKind: kind,
            number: noBa,
            issueDate: bordereau.DateVente,
            sourceReference: SourceRefBaPrefix + noBa,
            supplier: null,
            totals: new PivotTotalsDto(
                totalNet: PivotRounding.RoundAmount(totalNet),
                totalTax: PivotRounding.RoundAmount(totalTax),
                totalGross: PivotRounding.RoundAmount(totalNet + totalTax),
                sourceTotalGross: RoundAmount(bordereau.TotalBordereau)),
            operationCategory: null,
            currencyCode: NormalizeCurrency(bordereau.CodeDevise),
            customer: MapBuyer(bordereau),
            lines: lines,
            creditNoteRefs: creditNoteRefs,
            payments: null,
            documentCharges: null,
            invoicer: null,
            payee: null,
            isSelfBilled: false,
            prepaidAmount: null,
            sourceData: BuildBaDocumentSourceData(bordereau),
            paymentDueDate: null);
    }

    /// <summary>
    /// Mappe un bordereau VENDEUR (vente ou avoir) en document pivot — jambe VENDEUR de la marge.
    /// Lines = adjudication des lots (type 1) ; SellerFees = commission vendeur (type 2, TTC). Le destinataire
    /// est le VENDEUR (commettant). Pour un avoir, <paramref name="creditNoteOrigin"/> DOIT être résolu.
    /// </summary>
    /// <param name="bordereau">Le bordereau vendeur source (avec ses lignes type 1 et 2).</param>
    /// <param name="creditNoteOrigin">Bordereau vendeur d'origine d'un avoir, sinon <c>null</c>.</param>
    /// <returns>Le document pivot correspondant.</returns>
    public static PivotDocumentDto MapBvDocument(EncheresV6BordereauVendeur bordereau, EncheresV6BordereauVendeur? creditNoteOrigin)
    {
        if (bordereau is null)
        {
            throw new ArgumentNullException(nameof(bordereau));
        }

        string kind = RequireField(bordereau.BordereauOuAvoir, "bordereau_ou_avoir", bordereau.NoBv);
        string noBv = RequireField(bordereau.NoBv, "no_bv", bordereau.NoBv);
        RequireDate(bordereau.DateVente, "date_vente", noBv);

        string? regime = NullIfBlank(bordereau.CodeRegimeTva);
        var lines = new List<PivotLineDto>();
        var sellerFees = new List<PivotSellerFeeDto>();
        decimal totalNet = 0m;

        foreach (EncheresV6LigneVendeur ligne in bordereau.Lignes)
        {
            if (IsLotLineBv(ligne.TypeLigne))
            {
                decimal adjNet = RoundAmount(ligne.MontantAdjHt);
                totalNet += adjNet;
                lines.Add(new PivotLineDto(
                    description: LineDescription(ligne.Designation, "Adjudication", ligne.NoLignePv),
                    netAmount: adjNet,
                    quantity: 1m,
                    unitPriceNet: null,
                    sourceRegimeCodes: RegimeCodes(regime),
                    taxes: new[] { new PivotLineTaxDto(taxAmount: 0m, rate: null, categoryCode: null, vatexCode: null) },
                    sourceLineRef: ligne.NoLignePv,
                    sourceData: BuildBvLineSourceData(ligne)));
            }
            else if (IsCommissionLineBv(ligne.TypeLigne))
            {
                // Commission vendeur (TTC = HT + TVA) = jambe vendeur de la marge, au grain bordereau (no_bv).
                // Arrondi par composante puis somme (CLAUDE.md n°1).
                sellerFees.Add(new PivotSellerFeeDto(
                    lotReference: noBv,
                    netAmount: RoundAmount(ligne.MttFraisHt) + RoundAmount(ligne.MttTvaFrais),
                    sourceRegimeCode: regime,
                    sourceLineRef: ligne.NoLignePv,
                    description: NullIfBlank(ligne.Designation)));
            }
        }

        PivotDocumentRefDto[] creditNoteRefs = MapBvCreditNoteRefs(bordereau, kind, creditNoteOrigin);

        return new PivotDocumentDto(
            sourceDocumentKind: kind,
            number: noBv,
            issueDate: bordereau.DateVente,
            sourceReference: SourceRefBvPrefix + noBv,
            supplier: null,
            totals: new PivotTotalsDto(
                totalNet: PivotRounding.RoundAmount(totalNet),
                totalTax: 0m,
                totalGross: PivotRounding.RoundAmount(totalNet),
                sourceTotalGross: RoundAmount(bordereau.TotalBordereau)),
            operationCategory: null,
            currencyCode: NormalizeCurrency(bordereau.CodeDevise),
            customer: MapSeller(bordereau),
            lines: lines,
            creditNoteRefs: creditNoteRefs,
            payments: null,
            documentCharges: null,
            invoicer: null,
            payee: null,
            isSelfBilled: false,
            prepaidAmount: null,
            sourceData: BuildBvDocumentSourceData(bordereau),
            paymentDueDate: null,
            sellerFees: sellerFees);
    }

    /// <summary>
    /// Mappe une FACTURE CLIENT (facture ou avoir) en document pivot ORDINAIRE — document que l'OVV émet
    /// DIRECTEMENT, HORS mécanisme d'enchères opaque (AUCUN frais d'enchères : ni <c>BuyerFees</c> ni
    /// <c>SellerFees</c> — discriminant « document ordinaire » de <c>B2cPlainTaxableMarking</c>). Lines =
    /// lignes FACTURÉES (type 1, hors lignes de pur commentaire) au PRIX TOTAL : <c>netAmount</c> = HT ligne
    /// (<c>qte × prix_unitaire_ht</c>), TVA ligne = HT × <c>taux_tva</c> (la source elle-même calcule ainsi —
    /// transport, pas une règle inventée). Clé de régime = le TAUX (<c>taux_tva</c>) formaté brut (PAS
    /// <c>code_tva</c>, NON fiable dans la donnée — il diverge du taux appliqué ; conservé en SourceData) ; même
    /// clé de taux UNIFIÉE que les notes — la plateforme mappe la catégorie/taux par la table validée. La NATURE
    /// d'opération (TLB1 bien / TPS1 service) est laissée à la
    /// PLATEFORME (operationCategory <c>null</c>, remplie au read-time depuis le profil tenant — parité BA,
    /// CLAUDE.md n°6). Pour un avoir, <paramref name="creditNoteOrigin"/> DOIT être l'origine résolue.
    /// </summary>
    /// <param name="facture">La facture client source (avec ses lignes type 1).</param>
    /// <param name="creditNoteOrigin">Facture d'origine d'un avoir, sinon <c>null</c>.</param>
    /// <returns>Le document pivot correspondant.</returns>
    public static PivotDocumentDto MapFactureClientDocument(EncheresV6FactureClient facture, EncheresV6FactureClient? creditNoteOrigin)
    {
        if (facture is null)
        {
            throw new ArgumentNullException(nameof(facture));
        }

        string kind = RequireField(facture.FactureOuAvoir, "facture_ou_avoir", facture.NoFact);
        string noFact = RequireField(facture.NoFact, "no_fact", facture.NoFact);
        RequireDate(facture.DateFact, "date_fact", noFact);

        var lines = new List<PivotLineDto>();
        decimal totalNet = 0m;
        decimal totalTax = 0m;

        foreach (EncheresV6FactureClientLigne ligne in facture.Lignes)
        {
            // L'extracteur ne charge que les lignes FACTURÉES (type 1) ; on reste défensif et on écarte les
            // lignes de pur commentaire (TXT : quantité ET prix nuls) — présentation, pas une ligne facturée.
            if (!IsBilledLineFc(ligne.TypeLigne) || IsCommentLineFc(ligne))
            {
                continue;
            }

            decimal lineNet = RoundAmount(ligne.Qte * ligne.PrixUnitaireHt);

            // TVA de la ligne reconstruite comme la source la calcule (HT × taux_tva) : transport d'une
            // arithmétique source (taux_tva est un champ source), JAMAIS une règle fiscale inventée (CLAUDE.md
            // n°6). La plateforme reprend cette TVA telle quelle (ADR-0015). Arrondi half-up au centime (n°1).
            decimal lineTax = PivotRounding.RoundAmount(lineNet * (decimal)ligne.TauxTva / 100m);
            totalNet += lineNet;
            totalTax += lineTax;

            // Clé de régime = TAUX effectif de la ligne (taux_tva source, formaté brut), PAS code_tva : code_tva
            // est NON FIABLE dans la donnée réelle (il diverge du taux appliqué — p. ex. code_tva=0 avec taux 20 %,
            // ou code_tva=1 avec taux 0 %). Le taux PILOTE la TVA, c'est donc lui la vérité — même clé unifiée que
            // les notes. code_tva reste en SourceData (audit). La plateforme tranche la catégorie (table validée, R3).
            lines.Add(new PivotLineDto(
                description: LineDescription(ligne.Designation, "Ligne", ligne.NoLigne),
                netAmount: lineNet,
                quantity: ligne.Qte,
                unitPriceNet: null,
                sourceRegimeCodes: RegimeCodes(FormatRateToken((decimal)ligne.TauxTva)),
                taxes: new[] { new PivotLineTaxDto(taxAmount: lineTax, rate: null, categoryCode: null, vatexCode: null) },
                sourceLineRef: ligne.NoLigne,
                sourceData: BuildFactureLineSourceData(ligne)));
        }

        PivotDocumentRefDto[] creditNoteRefs = MapFactureCreditNoteRefs(facture, kind, creditNoteOrigin);

        return new PivotDocumentDto(
            sourceDocumentKind: kind,
            number: noFact,
            issueDate: facture.DateFact,
            sourceReference: SourceRefFcPrefix + noFact,
            supplier: null,
            totals: new PivotTotalsDto(
                totalNet: PivotRounding.RoundAmount(totalNet),
                totalTax: PivotRounding.RoundAmount(totalTax),
                totalGross: PivotRounding.RoundAmount(totalNet + totalTax),
                sourceTotalGross: RoundAmount(facture.MontantTtc)),
            operationCategory: null,
            currencyCode: NormalizeCurrency(facture.CodeDevise),
            customer: MapFactureClientCustomer(facture),
            lines: lines,
            creditNoteRefs: creditNoteRefs,
            payments: null,
            documentCharges: null,
            invoicer: null,
            payee: null,
            isSelfBilled: false,
            prepaidAmount: null,
            sourceData: BuildFactureDocumentSourceData(facture),
            paymentDueDate: null);
    }

    /// <summary>
    /// Mappe une NOTE D'HONORAIRES d'inventaire (note ou avoir) en document pivot ORDINAIRE — prestation de
    /// services autonome que l'OVV facture DIRECTEMENT (souvent un inventaire judiciaire ou volontaire), HORS
    /// mécanisme d'enchères opaque (AUCUN frais d'enchères : ni <c>BuyerFees</c> ni <c>SellerFees</c>). Lines =
    /// lignes FACTURÉES (honoraires type 1 + frais type 2 ; règlements type 3 exclus) au PRIX TOTAL :
    /// <c>netAmount</c> = HT ligne (<c>montant_ht</c>), TVA ligne = <c>montant_tva</c> (distincte, telle quelle).
    /// La note ne portant AUCUN <c>code_tva</c>, la clé de régime est le TAUX EFFECTIF recouvré par ligne
    /// (<see cref="RecoverRateToken"/>) — la plateforme tranche la catégorie/taux par la table validée (R3,
    /// arbitrage PO). La NATURE d'opération (TPS1 service) est laissée à la PLATEFORME (operationCategory
    /// <c>null</c>, parité BA — CLAUDE.md n°6). Pour un avoir, <paramref name="creditNoteOrigin"/> DOIT être résolu.
    /// </summary>
    /// <param name="note">La note d'honoraires source (avec ses lignes type 1/2).</param>
    /// <param name="creditNoteOrigin">Note d'origine d'un avoir, sinon <c>null</c>.</param>
    /// <returns>Le document pivot correspondant.</returns>
    public static PivotDocumentDto MapNoteHonoDocument(EncheresV6NoteHono note, EncheresV6NoteHono? creditNoteOrigin)
    {
        if (note is null)
        {
            throw new ArgumentNullException(nameof(note));
        }

        string kind = RequireField(note.FactureOuAvoir, "facture_ou_avoir", note.NoNoteHono);
        string noNote = RequireField(note.NoNoteHono, "no_note_hono", note.NoNoteHono);
        RequireDate(note.DateFacture, "date_facture", noNote);

        var lines = new List<PivotLineDto>();
        decimal totalNet = 0m;
        decimal totalTax = 0m;

        foreach (EncheresV6NoteHonoLigne ligne in note.Lignes)
        {
            // Honoraires (type 1) + frais (type 2) = lignes facturées ; le règlement (type 3) est exclu.
            if (!IsBilledLineNh(ligne.TypeLigne))
            {
                continue;
            }

            // HT et TVA distincte sont portés EXPLICITEMENT par la ligne source (montant_ht / montant_tva) :
            // arrondi half-up au centime (CLAUDE.md n°1), aucun recalcul. La TVA est reprise telle quelle.
            decimal lineNet = RoundAmount(ligne.MontantHt);
            decimal lineTax = RoundAmount(ligne.MontantTva);
            totalNet += lineNet;
            totalTax += lineTax;

            lines.Add(new PivotLineDto(
                description: LineDescription(ligne.Libelle, "Ligne", null),
                netAmount: lineNet,
                quantity: 1m,
                unitPriceNet: null,
                sourceRegimeCodes: RegimeCodes(RecoverRateToken(lineNet, lineTax)),
                taxes: new[] { new PivotLineTaxDto(taxAmount: lineTax, rate: null, categoryCode: null, vatexCode: null) },
                sourceLineRef: null,
                sourceData: BuildNoteLineSourceData(ligne)));
        }

        PivotDocumentRefDto[] creditNoteRefs = MapNoteCreditNoteRefs(note, kind, creditNoteOrigin);

        return new PivotDocumentDto(
            sourceDocumentKind: kind,
            number: noNote,
            issueDate: note.DateFacture,
            sourceReference: SourceRefNhPrefix + noNote,
            supplier: null,
            totals: new PivotTotalsDto(
                totalNet: PivotRounding.RoundAmount(totalNet),
                totalTax: PivotRounding.RoundAmount(totalTax),
                totalGross: PivotRounding.RoundAmount(totalNet + totalTax),
                sourceTotalGross: RoundAmount(note.MontantTtc)),
            operationCategory: null,
            currencyCode: NormalizeCurrency(note.CodeDevise),
            customer: MapNoteHonoCustomer(note),
            lines: lines,
            creditNoteRefs: creditNoteRefs,
            payments: null,
            documentCharges: null,
            invoicer: null,
            payee: null,
            isSelfBilled: false,
            prepaidAmount: null,
            sourceData: BuildNoteDocumentSourceData(note),
            paymentDueDate: null);
    }

    /// <summary>Mappe une ligne de règlement (BA type 3) en encaissement pivot brut (F09).</summary>
    /// <param name="bordereau">Le bordereau (entête minimale : no_ba) auquel le règlement est rattaché.</param>
    /// <param name="reglement">La ligne de règlement (type 3).</param>
    /// <returns>L'encaissement pivot correspondant.</returns>
    public static PivotPaymentDto MapPayment(EncheresV6Bordereau bordereau, EncheresV6Ligne reglement)
    {
        if (bordereau is null)
        {
            throw new ArgumentNullException(nameof(bordereau));
        }

        if (reglement is null)
        {
            throw new ArgumentNullException(nameof(reglement));
        }

        if (!reglement.DateReglement.HasValue)
        {
            throw new SourceSchemaException(
                $"Règlement sans date (no_ba « {bordereau.NoBa} », ligne « {reglement.NoLignePv} ») : "
                + "schéma EncheresV6 incompatible. Vérifiez l'extraction des lignes type 3.");
        }

        return new PivotPaymentDto(
            paymentDate: reglement.DateReglement.Value,
            amount: RoundAmount(reglement.MontantLigne),
            method: NullIfBlank(reglement.Designation) ?? NullIfBlank(reglement.CodeLigne),
            relatedDocumentNumber: bordereau.NoBa,
            sourceReference: PaymentSourceRef(bordereau, reglement));
    }

    /// <summary>Indique si un type de ligne BA est une ligne de LOT (type 1 : adjudication + commission acheteur).</summary>
    /// <param name="typeLigne">Le type de ligne source brut.</param>
    /// <returns><c>true</c> pour le type 1.</returns>
    internal static bool IsLotLineBa(string? typeLigne) => typeLigne == EncheresV6Schema.LigneLotBa;

    /// <summary>Indique si un type de ligne BA est un règlement (type 3).</summary>
    /// <param name="typeLigne">Le type de ligne source brut.</param>
    /// <returns><c>true</c> pour le type 3.</returns>
    internal static bool IsPaymentLineBa(string? typeLigne) => typeLigne == EncheresV6Schema.LigneReglementBa;

    /// <summary>Indique si un type de ligne BV est une ligne de LOT (type 1 : adjudication).</summary>
    /// <param name="typeLigne">Le type de ligne source brut.</param>
    /// <returns><c>true</c> pour le type 1.</returns>
    internal static bool IsLotLineBv(string? typeLigne) => typeLigne == EncheresV6Schema.LigneLotBv;

    /// <summary>Indique si un type de ligne BV est une commission vendeur (type 2 : jambe vendeur de la marge).</summary>
    /// <param name="typeLigne">Le type de ligne source brut.</param>
    /// <returns><c>true</c> pour le type 2.</returns>
    internal static bool IsCommissionLineBv(string? typeLigne) => typeLigne == EncheresV6Schema.LigneCommissionBv;

    /// <summary>Indique si un type de ligne de facture client est une ligne FACTURÉE (type 1).</summary>
    /// <param name="typeLigne">Le type de ligne source brut.</param>
    /// <returns><c>true</c> pour le type 1.</returns>
    internal static bool IsBilledLineFc(string? typeLigne) => typeLigne == EncheresV6Schema.LigneFactureeFc;

    /// <summary>
    /// Indique si une ligne FACTURÉE est une ligne de pur COMMENTAIRE (présentation) : quantité ET prix
    /// unitaire nuls (cas TXT du système source). Une telle ligne ne porte aucune valeur facturée — l'écarter
    /// est une discrimination STRUCTURELLE (quelle ligne est facturée), pas une dérivation fiscale (CLAUDE.md n°6).
    /// </summary>
    /// <param name="ligne">La ligne de facture client.</param>
    /// <returns><c>true</c> si la ligne est un commentaire sans montant.</returns>
    internal static bool IsCommentLineFc(EncheresV6FactureClientLigne ligne) =>
        ligne.Qte == 0 && ligne.PrixUnitaireHt == 0d;

    /// <summary>Indique si un type de ligne de note est FACTURÉ (honoraires type 1 OU frais type 2) ; le règlement (type 3) est exclu.</summary>
    /// <param name="typeLigne">Le type de ligne source brut.</param>
    /// <returns><c>true</c> pour le type 1 ou 2.</returns>
    internal static bool IsBilledLineNh(string? typeLigne) =>
        typeLigne == EncheresV6Schema.LigneHonoraireNh || typeLigne == EncheresV6Schema.LigneFraisNh;

    /// <summary>
    /// Recouvre le TAUX EFFECTIF d'une ligne de note (<c>TVA / HT × 100</c>) comme clé de régime BRUTE — la note
    /// d'honoraires ne porte AUCUN code de régime TVA NI taux explicite en source, le taux est donc reconstruit
    /// depuis deux champs source (arithmétique de TRANSPORT, jamais une catégorie fiscale : la plateforme tranche
    /// la catégorie via la table validée, arbitrage PO). Base nulle → « 0 » (taux non recouvrable sans base :
    /// ligne exonérée / hors champ, qui rend une note à taux MIXTES fail-closed côté plateforme).
    /// </summary>
    /// <param name="lineNet">HT de la ligne (arrondi au centime).</param>
    /// <param name="lineTax">TVA distincte de la ligne (arrondie au centime).</param>
    /// <returns>Le jeton de taux effectif (clé de régime brute).</returns>
    internal static string RecoverRateToken(decimal lineNet, decimal lineTax) =>
        lineNet == 0m ? "0" : FormatRateToken(lineTax / lineNet * 100m);

    /// <summary>
    /// Formate un TAUX de TVA (en %) en clé de régime BRUTE invariante (« 20 », « 5.5 », « 2.1 », « 0 ») — clé de
    /// régime UNIFIÉE factures + notes. Arrondi à 2 décimales (couvre les taux français 20 / 10 / 5.5 / 2.1 / 0)
    /// puis format sans zéro superflu. Transport d'un taux source (les factures portent <c>taux_tva</c> explicite ;
    /// les notes le recouvrent — voir <see cref="RecoverRateToken"/>), JAMAIS une catégorie fiscale : la plateforme
    /// tranche la catégorie via la table validée (R3, arbitrage PO).
    /// </summary>
    /// <param name="ratePercent">Le taux de TVA en pourcentage (p. ex. 20, 5.5, 0).</param>
    /// <returns>Le jeton de taux (clé de régime brute).</returns>
    internal static string FormatRateToken(decimal ratePercent) =>
        Math.Round(ratePercent, 2, MidpointRounding.AwayFromZero).ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>
    /// Conversion OBLIGATOIRE flottant legacy → <c>decimal</c> au centime, arrondi commercial (half-up) —
    /// CLAUDE.md n°1, ADR-0004 D3-7. NaN/Infini/hors-plage → <see cref="SourceSchemaException"/> typée.
    /// </summary>
    /// <param name="raw">Le montant brut (flottant source).</param>
    /// <returns>Le montant en <c>decimal</c> arrondi à 2 décimales (half-up).</returns>
    internal static decimal RoundAmount(double raw)
    {
        if (double.IsNaN(raw) || double.IsInfinity(raw))
        {
            throw new SourceSchemaException(
                $"Montant source illisible (NaN/Infini) : valeur brute « {raw} » reçue. "
                + "Document bloqué, jamais arrondi à l'aveugle (ADR-0004 D3-7). Vérifiez l'extraction des montants en source.");
        }

        decimal value;
        try
        {
            value = (decimal)raw;
        }
        catch (OverflowException ex)
        {
            throw new SourceSchemaException(
                $"Montant source hors de la plage decimal : valeur brute « {raw} » reçue. "
                + "Document bloqué (ADR-0004 D3-7). Vérifiez l'extraction des montants en source.",
                ex);
        }

        return PivotRounding.RoundAmount(value);
    }

    private static string[] RegimeCodes(string? codeRegime) =>
        string.IsNullOrWhiteSpace(codeRegime) ? Array.Empty<string>() : new[] { codeRegime!.Trim() };

    /// <summary>
    /// Jeton de ZONE d'un bordereau export, ou <c>null</c> si <c>code_export</c> est faux (cas nominal). Normalise
    /// le mode de livraison legacy (« HORS CEE » / « CEE » / « FRANCE » / autre) en zone — transport d'une donnée
    /// source (miroir de <see cref="NormalizeCountryCode"/>), JAMAIS une catégorie fiscale. La zone alimente la
    /// clé de régime par zone (F03 §2.8) ; c'est la table validée de la plateforme qui tranche la catégorie.
    /// <list type="bullet">
    ///   <item><c>HORSUE</c> — export hors UE (mode « HORS CEE ») → mappé `G`/0 % (262 I) ;</item>
    ///   <item><c>CEE</c> — livraison intra-UE (mode « CEE ») → mappé `K`/0 % (262 ter / 258 A) ;</item>
    ///   <item><c>FR</c> — franchise (mode « FRANCE » + <c>code_export</c> : achat en franchise art. 275) → `G`/0 %.</item>
    /// </list>
    /// </summary>
    private static string? ExportZone(EncheresV6Bordereau bordereau)
    {
        if (!bordereau.CodeExport)
        {
            return null;
        }

        string mode = (bordereau.ModeLivraison ?? string.Empty).Trim().ToUpperInvariant();
        if (mode.IndexOf("HORS", StringComparison.Ordinal) >= 0)
        {
            return "HORSUE";
        }

        if (mode.IndexOf("CEE", StringComparison.Ordinal) >= 0)
        {
            return "CEE";
        }

        return "FR";
    }

    /// <summary>
    /// Clé de régime par ZONE (RegimeKeyShape.Composite, F03 §2.8) : <c>code_regime</c> brut hors export, sinon
    /// <c>EXP_{zone}</c> (<c>EXP_HORSUE</c>/<c>EXP_CEE</c>/<c>EXP_FR</c>) — l'exonération internationale (262 I /
    /// 262 ter / 275) prime sur le régime domestique, donc la ZONE seule classe (une règle de mapping par zone, pas
    /// par couple régime×zone). Le régime brut reste dans <c>SourceData</c> (audit). La plateforme mappe cette clé
    /// via la table validée — une zone non couverte BLOQUE (fail-closed), jamais devinée.
    /// </summary>
    private static string? ComposeRegimeKey(string? codeRegime, string? exportZone)
    {
        if (exportZone is null)
        {
            return codeRegime;
        }

        return "EXP_" + exportZone;
    }

    private static string LineDescription(string? libelle, string fallback, string? noLignePv)
    {
        if (!string.IsNullOrWhiteSpace(libelle))
        {
            return libelle!.Trim();
        }

        return string.IsNullOrWhiteSpace(noLignePv) ? fallback : fallback + " lot " + noLignePv!.Trim();
    }

    // Libellé de la ligne d'HONORAIRE acheteur (rôle BuyerFee) : un libellé FIXE « Honoraires acheteur » (+ lot),
    // jamais la désignation du lot — sinon l'honoraire porterait le MÊME libellé que sa ligne d'adjudication.
    private static string HonoraireDescription(string? noLignePv) =>
        string.IsNullOrWhiteSpace(noLignePv) ? "Honoraires acheteur" : "Honoraires acheteur lot " + noLignePv!.Trim();

    private static PivotPartyDto? MapBuyer(EncheresV6Bordereau bordereau)
    {
        string? name = ComposeName(bordereau.Nom, bordereau.Prenom);
        if (name is null)
        {
            // B2C anonyme : pas d'acheteur nommé → pas de tiers destinataire (F01-F02 §6).
            return null;
        }

        return new PivotPartyDto(
            name: name,
            siren: NullIfBlank(bordereau.AcheteurSiren),
            vatNumber: NullIfBlank(bordereau.TvaCee),
            address: MapAddress(bordereau.Adresse, bordereau.CodePostal, bordereau.Ville, bordereau.CodePays),
            isCompanyHint: !string.IsNullOrWhiteSpace(bordereau.Societe));
    }

    private static PivotPartyDto? MapSeller(EncheresV6BordereauVendeur bordereau)
    {
        string? name = ComposeName(bordereau.Nom, bordereau.Prenom);
        if (name is null)
        {
            return null;
        }

        // Le destinataire du BV est le VENDEUR (commettant). Aucun SIREN n'est porté ici, même pour un commettant
        // ASSUJETTI (régime du prix total) : router le BV en B2B sur ce seul SIREN émettrait le DÉCOMPTE ENTIER
        // (adjudication = le bien + commission) comme facture office→vendeur, alors que seul l'HONORAIRE vendeur
        // devrait l'être (le bien relève de la jambe vendeur→office / autofacturation 389, F15). L'aiguillage B2B
        // de l'honoraire vendeur exige un document HONORAIRE-SEUL distinct — différé (hors BUG-8). Décision B2B/B2C
        // = plateforme (VAL05) ; ici, lecture BRUTE sans heuristique (CLAUDE.md n°6).
        return new PivotPartyDto(
            name: name,
            address: MapAddress(null, bordereau.CodePostal, bordereau.Ville, bordereau.CodePays),
            isCompanyHint: false);
    }

    private static PivotPartyDto? MapFactureClientCustomer(EncheresV6FactureClient facture)
    {
        string? name = ComposeName(facture.Nom, facture.Prenom);
        if (name is null)
        {
            // Client anonyme : pas de tiers destinataire nommé (F01-F02 §6).
            return null;
        }

        // La facture client ne porte ni SIREN ni champ « société » en source → tiers PARTICULIER (B2C) par
        // construction. L'aiguillage B2B/B2C reste une décision PLATEFORME (VAL05) ; ici, lecture BRUTE sans
        // heuristique (CLAUDE.md n°6) — IsCompanyHint false, aucun SIREN deviné.
        return new PivotPartyDto(
            name: name,
            address: MapAddress(facture.Adresse1, facture.Cp, facture.Ville, facture.CodePays),
            isCompanyHint: false);
    }

    private static PivotPartyDto? MapNoteHonoCustomer(EncheresV6NoteHono note)
    {
        string? name = ComposeName(note.Nom, note.Prenom);
        if (name is null)
        {
            return null;
        }

        // La note d'honoraires ne porte ni SIREN ni champ « société » en source → tiers lu B2C par construction
        // (IsCompanyHint false, aucun SIREN deviné). Un destinataire professionnel (forme juridique dans le nom)
        // est traité par la PLATEFORME (BuyerLooksProfessionalRule / aiguillage VAL05), jamais ici (CLAUDE.md n°6).
        return new PivotPartyDto(
            name: name,
            address: MapAddress(note.Adresse, note.CodePostal, note.Ville, note.CodePays),
            isCompanyHint: false);
    }

    private static PivotAddressDto? MapAddress(string? line1, string? postalCode, string? city, string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(line1)
            && string.IsNullOrWhiteSpace(postalCode)
            && string.IsNullOrWhiteSpace(city)
            && string.IsNullOrWhiteSpace(countryCode))
        {
            return null;
        }

        return new PivotAddressDto(
            line1: NullIfBlank(line1),
            postalCode: NullIfBlank(postalCode),
            city: NullIfBlank(city),
            countryCode: NullIfBlank(NormalizeCountryCode(countryCode)));
    }

    /// <summary>
    /// Normalise un code pays BRUT de la source EncheresV6 (<c>code_pays</c>) vers ISO 3166-1 alpha-2 via la table
    /// de correspondance <see cref="NonIsoCountryCodeMap"/> (extensible, BUG-18). Tout code NON présent dans la
    /// table est laissé STRICTEMENT BRUT (fail-closed, CLAUDE.md n°2) : l'adaptateur ne devine jamais un pays — un
    /// code inconnu remonte tel quel à la plateforme, qui tranche/BLOQUE en validation (BT-55, BuyerIdentityRule).
    /// Important : le pays pilote l'aiguillage fiscal (UE vs hors UE) — une normalisation fausse mis-route ; elle
    /// doit donc être DÉCLARÉE dans la table, jamais inférée.
    /// </summary>
    /// <param name="raw">Le code pays brut tel que stocké en source (<c>code_pays</c>), éventuellement <c>null</c>.</param>
    /// <returns>Le code ISO alpha-2 mappé pour un code legacy connu ; sinon la valeur d'origine inchangée.</returns>
    private static string? NormalizeCountryCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return raw;
        }

        return NonIsoCountryCodeMap.TryGetValue(raw!.Trim().ToUpperInvariant(), out var iso) ? iso : raw;
    }

    private static PivotDocumentRefDto[] MapBaCreditNoteRefs(EncheresV6Bordereau bordereau, string kind, EncheresV6Bordereau? origin)
    {
        if (kind != PieceAvoir)
        {
            return Array.Empty<PivotDocumentRefDto>();
        }

        if (origin is null)
        {
            throw new SourceSchemaException(
                $"Avoir acheteur (no_ba « {bordereau.NoBa} ») sans bordereau d'origine résoluble "
                + $"(no_ba_lettrage « {bordereau.NoBaLettrage} ») : document bloqué, l'origine n'est jamais devinée (ADR-0004 D3-3).");
        }

        string originNoBa = RequireField(origin.NoBa, "no_ba (origine avoir)", origin.NoBa);
        RequireDate(origin.DateVente, "date_vente (origine avoir)", originNoBa);

        return new[] { new PivotDocumentRefDto(originNoBa, origin.DateVente, SourceRefBaPrefix + originNoBa) };
    }

    private static PivotDocumentRefDto[] MapBvCreditNoteRefs(EncheresV6BordereauVendeur bordereau, string kind, EncheresV6BordereauVendeur? origin)
    {
        if (kind != PieceAvoir)
        {
            return Array.Empty<PivotDocumentRefDto>();
        }

        if (origin is null)
        {
            throw new SourceSchemaException(
                $"Avoir vendeur (no_bv « {bordereau.NoBv} ») sans bordereau d'origine résoluble "
                + $"(no_bv_lettrage « {bordereau.NoBvLettrage} ») : document bloqué, l'origine n'est jamais devinée (ADR-0004 D3-3).");
        }

        string originNoBv = RequireField(origin.NoBv, "no_bv (origine avoir)", origin.NoBv);
        RequireDate(origin.DateVente, "date_vente (origine avoir)", originNoBv);

        return new[] { new PivotDocumentRefDto(originNoBv, origin.DateVente, SourceRefBvPrefix + originNoBv) };
    }

    private static PivotDocumentRefDto[] MapFactureCreditNoteRefs(EncheresV6FactureClient facture, string kind, EncheresV6FactureClient? origin)
    {
        if (kind != PieceAvoir)
        {
            return Array.Empty<PivotDocumentRefDto>();
        }

        if (origin is null)
        {
            throw new SourceSchemaException(
                $"Avoir facture client (no_fact « {facture.NoFact} ») sans facture d'origine résoluble "
                + $"(no_facture_lettrage « {facture.NoFactureLettrage} ») : document bloqué, l'origine n'est jamais devinée (ADR-0004 D3-3).");
        }

        string originNoFact = RequireField(origin.NoFact, "no_fact (origine avoir)", origin.NoFact);
        RequireDate(origin.DateFact, "date_fact (origine avoir)", originNoFact);

        return new[] { new PivotDocumentRefDto(originNoFact, origin.DateFact, SourceRefFcPrefix + originNoFact) };
    }

    private static PivotDocumentRefDto[] MapNoteCreditNoteRefs(EncheresV6NoteHono note, string kind, EncheresV6NoteHono? origin)
    {
        if (kind != PieceAvoir)
        {
            return Array.Empty<PivotDocumentRefDto>();
        }

        if (origin is null)
        {
            throw new SourceSchemaException(
                $"Avoir de note d'honoraires (no_note_hono « {note.NoNoteHono} ») sans note d'origine résoluble "
                + $"(no_note_lettrage « {note.NoNoteLettrage} ») : document bloqué, l'origine n'est jamais devinée (ADR-0004 D3-3).");
        }

        string originNoNote = RequireField(origin.NoNoteHono, "no_note_hono (origine avoir)", origin.NoNoteHono);
        RequireDate(origin.DateFacture, "date_facture (origine avoir)", originNoNote);

        return new[] { new PivotDocumentRefDto(originNoNote, origin.DateFacture, SourceRefNhPrefix + originNoNote) };
    }

    private static string? ComposeName(string? nom, string? prenom)
    {
        string n = (nom ?? string.Empty).Trim();
        string p = (prenom ?? string.Empty).Trim();
        if (n.Length == 0 && p.Length == 0)
        {
            return null;
        }

        return p.Length == 0 ? n : (n.Length == 0 ? p : n + " " + p);
    }

    private static string NormalizeCurrency(string? code)
    {
        var trimmed = code?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return DeviseDomestique;
        }

        // Le système source étiquette l'euro « EURO » (libellé Magic/Zen, non-ISO) : normalisé vers l'ISO 4217
        // « EUR ». C'est une normalisation de FORMAT du code devise (pas une interprétation fiscale, R3) : la
        // plateforme exige un code ISO 4217 valide, sinon elle bloque le document. Tout autre code est transporté
        // tel quel (la plateforme tranchera) — seul l'alias connu « EURO » de la source est rapproché de l'ISO.
        // trimmed est non-null ici (retour anticipé ci-dessus) ; net48 n'annote pas IsNullOrEmpty → « ! » explicite.
        return string.Equals(trimmed, "EURO", StringComparison.OrdinalIgnoreCase) ? DeviseDomestique : trimmed!;
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value!.Trim();

    private static string PaymentSourceRef(EncheresV6Bordereau bordereau, EncheresV6Ligne reglement)
    {
        if (!string.IsNullOrWhiteSpace(reglement.NoRemise))
        {
            return "encheresv6:remise:" + reglement.NoRemise!.Trim();
        }

        return SourceRefBaPrefix + (bordereau.NoBa ?? string.Empty) + "/ligne=" + (reglement.NoLignePv ?? string.Empty);
    }

    private static string RequireField(string? value, string fieldName, string? noRef)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new SourceSchemaException(
                $"Champ source obligatoire « {fieldName} » absent (réf. « {noRef} ») : schéma EncheresV6 "
                + "incompatible. Vérifiez la requête d'extraction.");
        }

        return value!.Trim();
    }

    private static void RequireDate(DateTime value, string fieldName, string? noRef)
    {
        if (value == default(DateTime))
        {
            throw new SourceSchemaException(
                $"Champ source obligatoire « {fieldName} » absent ou invalide (réf. « {noRef} ») : "
                + "la date n'est jamais devinée (ADR-0004 D3-3). Vérifiez l'extraction des données source.");
        }
    }

    private static string BuildBaLineSourceData(EncheresV6Ligne ligne) =>
        JsonConvert.SerializeObject(new
        {
            no_ligne_pv = ligne.NoLignePv,
            no_ligne_tout_pv = ligne.NoLigneToutPv,
            montant_adj_ht_brut = ligne.MontantAdjHt,
            mtt_tva_inclus_adj_brut = ligne.MttTvaInclusAdj,
            mtt_tva_en_plus_adj_brut = ligne.MttTvaEnPlusAdj,
            montant_frais_ht_brut = ligne.MontantFraisHt,
            montant_tva_frais_brut = ligne.MontantTvaFrais,
            code_regime = ligne.CodeRegime,
        });

    private static string BuildBvLineSourceData(EncheresV6LigneVendeur ligne) =>
        JsonConvert.SerializeObject(new
        {
            no_ligne_pv = ligne.NoLignePv,
            montant_adj_ht_brut = ligne.MontantAdjHt,
            mtt_frais_ht_brut = ligne.MttFraisHt,
            mtt_tva_frais_brut = ligne.MttTvaFrais,
        });

    private static string BuildBaDocumentSourceData(EncheresV6Bordereau bordereau) =>
        JsonConvert.SerializeObject(new
        {
            no_ba = bordereau.NoBa,
            total_bordereau_brut = bordereau.TotalBordereau,
            code_export_brut = bordereau.CodeExport,
            mode_livraison_brut = bordereau.ModeLivraison,
        });

    private static string BuildBvDocumentSourceData(EncheresV6BordereauVendeur bordereau) =>
        JsonConvert.SerializeObject(new
        {
            no_bv = bordereau.NoBv,
            total_bordereau_brut = bordereau.TotalBordereau,
            code_regime_tva = bordereau.CodeRegimeTva,
        });

    private static string BuildFactureLineSourceData(EncheresV6FactureClientLigne ligne) =>
        JsonConvert.SerializeObject(new
        {
            no_ligne = ligne.NoLigne,
            code_article = ligne.CodeArticle,
            qte = ligne.Qte,
            prix_unitaire_ht_brut = ligne.PrixUnitaireHt,
            code_tva = ligne.CodeTva,
            taux_tva_brut = ligne.TauxTva,
        });

    private static string BuildFactureDocumentSourceData(EncheresV6FactureClient facture) =>
        JsonConvert.SerializeObject(new
        {
            no_fact = facture.NoFact,
            montant_ht_brut = facture.MontantHt,
            montant_tva_brut = facture.MontantTva,
            montant_ttc_brut = facture.MontantTtc,
        });

    private static string BuildNoteLineSourceData(EncheresV6NoteHonoLigne ligne) =>
        JsonConvert.SerializeObject(new
        {
            type_ligne = ligne.TypeLigne,
            code_ligne = ligne.CodeLigne,
            montant_ht_brut = ligne.MontantHt,
            montant_tva_brut = ligne.MontantTva,
        });

    private static string BuildNoteDocumentSourceData(EncheresV6NoteHono note) =>
        JsonConvert.SerializeObject(new
        {
            no_note_hono = note.NoNoteHono,
            montant_ttc_brut = note.MontantTtc,
        });
}
