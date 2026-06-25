namespace Liakont.Agent.Adapters.EncheresV6;

using System;
using System.Collections.Generic;
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

    private const string DeviseDomestique = "EUR";

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
        var buyerFees = new List<PivotBuyerFeeDto>();
        decimal totalNet = 0m;
        decimal totalTax = 0m;

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
            totalNet += adjNet;
            totalTax += adjTax;

            lines.Add(new PivotLineDto(
                description: LineDescription(ligne.Designation, "Adjudication", ligne.NoLignePv),
                netAmount: adjNet,
                quantity: 1m,
                unitPriceNet: null,
                sourceRegimeCodes: RegimeCodes(ligne.CodeRegime),
                taxes: new[] { new PivotLineTaxDto(taxAmount: adjTax, rate: null, categoryCode: null, vatexCode: null) },
                sourceLineRef: ligne.NoLignePv,
                sourceData: BuildBaLineSourceData(ligne)));

            // Commission acheteur (TTC = HT + TVA) = jambe acheteur de la marge, au grain bordereau (no_ba).
            // Arrondi par composante puis somme (CLAUDE.md n°1).
            buyerFees.Add(new PivotBuyerFeeDto(
                lotReference: noBa,
                netAmount: RoundAmount(ligne.MontantFraisHt) + RoundAmount(ligne.MontantTvaFrais),
                sourceRegimeCode: NullIfBlank(ligne.CodeRegime),
                sourceLineRef: ligne.NoLignePv,
                description: NullIfBlank(ligne.Designation)));
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
            paymentDueDate: null,
            buyerFees: buyerFees);
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

    private static string LineDescription(string? libelle, string fallback, string? noLignePv)
    {
        if (!string.IsNullOrWhiteSpace(libelle))
        {
            return libelle!.Trim();
        }

        return string.IsNullOrWhiteSpace(noLignePv) ? fallback : fallback + " lot " + noLignePv!.Trim();
    }

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

        // Sous le régime de la marge, le vendeur (commettant) est un non assujetti : aucun SIREN porté,
        // aucun indice société (pas de colonne société côté BV) — décision B2B/B2C = plateforme (VAL05).
        return new PivotPartyDto(
            name: name,
            address: MapAddress(null, bordereau.CodePostal, bordereau.Ville, bordereau.CodePays),
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
    /// Normalise un code pays BRUT de la source EncheresV6 (<c>code_pays</c>) vers ISO 3166-1 alpha-2 pour les
    /// SEULS cas non-ISO connus et sûrs de la base réelle : les nations du Royaume-Uni (<c>ENG</c>/<c>SCO</c>/
    /// <c>WAL</c>/<c>NIR</c> — codes de subdivision ISO 3166-2, pas des pays alpha-2) relèvent toutes de
    /// <c>GB</c> (ISO 3166-1). Normalisation de DONNÉE legacy (miroir du nettoyage devise « EURO » → « EUR »),
    /// jamais une règle fiscale (aucune catégorie TVA / VATEX / seuil inventé — CLAUDE.md n°2). Tout code NON
    /// listé est laissé STRICTEMENT BRUT : l'adaptateur ne devine jamais un pays — un code inconnu remonte tel
    /// quel à la plateforme, qui tranche en validation (BT-55, BuyerIdentityRule).
    /// </summary>
    /// <param name="raw">Le code pays brut tel que stocké en source (<c>code_pays</c>), éventuellement <c>null</c>.</param>
    /// <returns><c>GB</c> pour <c>ENG</c>/<c>SCO</c>/<c>WAL</c>/<c>NIR</c> ; sinon la valeur d'origine inchangée.</returns>
    private static string? NormalizeCountryCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return raw;
        }

        switch (raw!.Trim().ToUpperInvariant())
        {
            case "ENG":
            case "SCO":
            case "WAL":
            case "NIR":
                return "GB";
            default:
                return raw;
        }
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
        });

    private static string BuildBvDocumentSourceData(EncheresV6BordereauVendeur bordereau) =>
        JsonConvert.SerializeObject(new
        {
            no_bv = bordereau.NoBv,
            total_bordereau_brut = bordereau.TotalBordereau,
            code_regime_tva = bordereau.CodeRegimeTva,
        });
}
