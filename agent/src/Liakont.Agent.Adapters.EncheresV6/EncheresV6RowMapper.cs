namespace Liakont.Agent.Adapters.EncheresV6;

using System;
using System.Collections.Generic;
using Liakont.Agent.Adapters.EncheresV6.Source;
using Liakont.Agent.Contracts;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Core.Extraction;
using Newtonsoft.Json;

/// <summary>
/// Transforme les lignes BRUTES d'EncheresV6 (<c>entete_ba</c> / <c>lignes_ba</c> / <c>Regime_tva</c>)
/// en modèle pivot EN 16931. C'est LA transformation partagée entre le
/// <see cref="EncheresV6FixtureExtractor"/> (rejeu de fixtures) et le futur PervasiveExtractor
/// (ODBC réel, ADP02) — seule la source des lignes change. Le mapper respecte strictement le contrat
/// d'extraction (F01-F02 §4.2) :
/// <list type="bullet">
///   <item>il ne mappe PAS la TVA (R3) : <c>SourceRegimeCodes</c> bruts, <c>CategoryCode</c>/<c>VatexCode</c>
///   laissés nuls (le mapping F03 est plateforme) ;</item>
///   <item>il ne valide PAS (R4) et ne classe PAS facture/avoir : <c>SourceDocumentKind</c> brut
///   (ADR-0004 D3-3) ;</item>
///   <item>il ne calcule aucun montant (les montants viennent de la source) ; la SEULE arithmétique
///   autorisée et obligatoire est la conversion des flottants legacy en <c>decimal</c> arrondi au
///   centime (half-up), l'original étant conservé dans <c>SourceData</c> (ADR-0004 D3-7,
///   CLAUDE.md n°1).</item>
/// </list>
/// La <see cref="PivotDocumentDto.OperationCategory"/> est une donnée de PARAMÉTRAGE de la source
/// (fournie par l'appelant) et non une règle dérivée : la nature exacte d'un bordereau d'enchères
/// (livraison de biens / prestation / mixte) est une décision de l'expert-comptable du tenant
/// (F01-F02 §7 #3, NON tranchée) — l'adaptateur ne la devine jamais (CLAUDE.md n°2).
/// </summary>
internal static class EncheresV6RowMapper
{
    /// <summary>Type de ligne « adjudication » (livraison du bien adjugé) — F01-F02 §4.3.</summary>
    internal const string LigneAdjudication = "4";

    /// <summary>Type de ligne « frais » (frais acheteur) — F01-F02 §4.3.</summary>
    internal const string LigneFrais = "2";

    /// <summary>Type de ligne « règlement » (encaissement, F09) — F01-F02 §4.3.</summary>
    internal const string LigneReglement = "3";

    /// <summary>
    /// Type de ligne « frais vendeur » (commission vendeur, bordereau vendeur / BV) — F01-F02 §4.3.1, B2C-06.
    /// DONNÉE DE CALCUL de marge (e-reporting B2C), JAMAIS une ligne facturée à l'acheteur : l'extraction
    /// document (<see cref="IsDocumentLine"/>) l'ignore (art. 297 E, B2C-08). Lue séparément par
    /// <c>ExtractSellerFees</c> (B2C-07), rattachée au bordereau via le <c>no_ba</c> existant — option (a)
    /// tranchée par B2C-06, sans jointure inventée.
    /// </summary>
    internal const string LigneFraisVendeur = "5";

    /// <summary>Type de pièce source « bordereau de vente ».</summary>
    internal const string PieceVente = "B";

    /// <summary>Type de pièce source « avoir ».</summary>
    internal const string PieceAvoir = "A";

    /// <summary>
    /// Préfixe de la référence source d'un document (<see cref="PivotDocumentDto.SourceReference"/> =
    /// <c>"no_ba=&lt;valeur&gt;"</c>). Source de vérité UNIQUE du format, partagée avec
    /// <see cref="FileSystemEncheresV6PdfSource"/> (qui en extrait le <c>no_ba</c> pour retrouver les PDF
    /// liés d'un document, ADP05) : la production (<see cref="SourceRef"/>) et la consommation restent en phase.
    /// </summary>
    internal const string SourceReferencePrefix = "no_ba=";

    private const string DeviseDomestique = "EUR";

    /// <summary>
    /// Mappe un bordereau (vente ou avoir) en document pivot. Pour un avoir
    /// (<c>bordereau_ou_avoir = "A"</c>), <paramref name="creditNoteOrigin"/> DOIT être le bordereau
    /// d'origine résolu via <c>no_ba_lettrage</c> : un avoir sans origine résoluble est bloqué
    /// (<see cref="SourceSchemaException"/>), jamais deviné (ADR-0004 D3-3, CLAUDE.md n°3).
    /// </summary>
    /// <param name="bordereau">Le bordereau source à mapper.</param>
    /// <param name="emitter">Identité de l'émetteur (paramétrage tenant — SIREN absent de la base).</param>
    /// <param name="operationCategory">Nature d'opération (paramétrage de la source — F01-F02 §7 #3).</param>
    /// <param name="creditNoteOrigin">Bordereau d'origine d'un avoir (résolu par l'extracteur), sinon <c>null</c>.</param>
    /// <returns>Le document pivot correspondant.</returns>
    public static PivotDocumentDto MapDocument(
        EncheresV6Bordereau bordereau,
        EncheresV6EmitterIdentity emitter,
        OperationCategory operationCategory,
        EncheresV6Bordereau? creditNoteOrigin)
    {
        if (bordereau is null)
        {
            throw new ArgumentNullException(nameof(bordereau));
        }

        if (emitter is null)
        {
            throw new ArgumentNullException(nameof(emitter));
        }

        string kind = RequireField(bordereau.BordereauOuAvoir, "bordereau_ou_avoir", bordereau.NoBa);
        string number = RequireField(bordereau.NumeroPiece, "numero_piece", bordereau.NoBa);
        string noBa = RequireField(bordereau.NoBa, "no_ba", bordereau.NoBa);

        if (bordereau.DateVente == default(DateTime))
        {
            throw new SourceSchemaException(
                $"Champ source obligatoire « date_vente » absent ou invalide (no_ba « {noBa} ») : "
                + "la date est manquante ou illisible dans EncheresV6. Document bloqué, la date n'est "
                + "jamais devinée (ADR-0004 D3-3). Vérifiez l'extraction des données source.");
        }

        var lines = new List<PivotLineDto>();
        foreach (EncheresV6Ligne ligne in bordereau.Lignes)
        {
            if (IsDocumentLine(ligne.TypeLigne))
            {
                lines.Add(MapLine(ligne, noBa));
            }
        }

        PivotDocumentRefDto[] creditNoteRefs = MapCreditNoteRefs(bordereau, kind, creditNoteOrigin);

        // EN 16931 BT-9 (EXT01) : le schéma EncheresV6 ne documente AUCUNE colonne d'échéance de paiement
        // au niveau document (entete_ba / lignes_ba — F01-F02 §4.3 ; date_reglement est la date
        // d'ENCAISSEMENT d'un règlement type 3, PAS une échéance). Écart CONSIGNÉ (F01-F02 §4.3) :
        // paymentDueDate reste null, jamais de date fabriquée (CLAUDE.md n°2). Conséquence : une facture
        // EncheresV6 à montant dû positif (non soldée) reste rejetée par BR-CO-25 — comportement documenté ;
        // le jour où la source exposera l'échéance, mapper la colonne ICI (lecture seule stricte, CLAUDE.md n°5).
        return new PivotDocumentDto(
            sourceDocumentKind: kind,
            number: number,
            issueDate: bordereau.DateVente,
            sourceReference: SourceRef(noBa),
            supplier: MapEmitter(emitter),
            totals: new PivotTotalsDto(
                totalNet: RoundAmount(bordereau.TotalHt),
                totalTax: RoundAmount(bordereau.TotalTva),
                totalGross: RoundAmount(bordereau.TotalTtc),
                sourceTotalGross: RoundAmount(bordereau.TotalTtc)),
            operationCategory: operationCategory,
            currencyCode: DeviseDomestique,
            customer: MapCustomer(bordereau),
            lines: lines,
            creditNoteRefs: creditNoteRefs,
            payments: null,
            documentCharges: null,
            invoicer: null,
            payee: null,
            isSelfBilled: false,
            prepaidAmount: null,
            sourceData: BuildDocumentSourceData(bordereau),
            paymentDueDate: null);
    }

    /// <summary>
    /// Mappe une ligne de règlement (type 3) en encaissement pivot brut (F09). L'agrégation
    /// jour × taux est faite par la plateforme (PIP03) ; l'adaptateur transmet le paiement brut.
    /// </summary>
    /// <param name="bordereau">Le bordereau auquel le règlement est rattaché (lettrage).</param>
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
                $"Règlement sans date (no_ba « {bordereau.NoBa} », ligne « {reglement.NoLigne} ») : "
                + "schéma EncheresV6 incompatible. Vérifiez l'extraction des lignes type 3.");
        }

        return new PivotPaymentDto(
            paymentDate: reglement.DateReglement.Value,
            amount: RoundAmount(reglement.MontantHt),
            method: reglement.ModeReglement,
            relatedDocumentNumber: bordereau.NumeroPiece,
            sourceReference: PaymentSourceRef(bordereau, reglement));
    }

    /// <summary>
    /// Mappe une ligne de frais vendeur (type 5, bordereau vendeur) en enregistrement brut
    /// <see cref="EncheresV6SellerFee"/> (B2C-07). EXTRACTION PURE : aucune logique fiscale
    /// (CLAUDE.md n°6, R3) — le montant HT est la seule donnée de calcul portée, convertie en
    /// <c>decimal</c> au centime (half-up) à la frontière comme tout montant (CLAUDE.md n°1,
    /// ADR-0004 D3-7, parité avec <see cref="MapPayment"/>) ; le code régime est transporté BRUT.
    /// La TVA de la ligne n'est PAS modélisée : sous régime de la marge aucune TVA distincte ne figure
    /// (art. 297 E) et le frais vendeur n'est qu'un terme HT de la formule de marge (F03 §2.4, sourcée
    /// par B2C-05). Le rattachement au lot se fait par le <c>no_ba</c> du bordereau (grain bordereau —
    /// le <c>no_lot</c> présumé n'existe pas, F01-F02 §4.3.1).
    /// </summary>
    /// <param name="bordereau">Le bordereau (entête) auquel le frais vendeur est rattaché (par <c>no_ba</c>).</param>
    /// <param name="ligne">La ligne de frais vendeur (type 5).</param>
    /// <returns>L'enregistrement de frais vendeur brut correspondant.</returns>
    public static EncheresV6SellerFee MapSellerFee(EncheresV6Bordereau bordereau, EncheresV6Ligne ligne)
    {
        if (bordereau is null)
        {
            throw new ArgumentNullException(nameof(bordereau));
        }

        if (ligne is null)
        {
            throw new ArgumentNullException(nameof(ligne));
        }

        string noBa = RequireField(bordereau.NoBa, "no_ba", bordereau.NoBa);

        return new EncheresV6SellerFee(
            noBa: noBa,
            netAmount: RoundAmount(ligne.MontantHt),
            sourceRegimeCode: ligne.CodeRegime,
            sourceLineRef: ligne.NoLigne,
            description: ligne.Designation);
    }

    /// <summary>Indique si un type de ligne source est une ligne de document (adjudication ou frais).</summary>
    /// <param name="typeLigne">Le type de ligne source brut.</param>
    /// <returns><c>true</c> pour les types 4 (adjudication) et 2 (frais).</returns>
    internal static bool IsDocumentLine(string? typeLigne) =>
        typeLigne == LigneAdjudication || typeLigne == LigneFrais;

    /// <summary>Indique si un type de ligne source est un règlement (type 3).</summary>
    /// <param name="typeLigne">Le type de ligne source brut.</param>
    /// <returns><c>true</c> pour le type 3 (règlement).</returns>
    internal static bool IsPaymentLine(string? typeLigne) => typeLigne == LigneReglement;

    /// <summary>Indique si un type de ligne source est un frais vendeur (type 5, bordereau vendeur — B2C-06/07).</summary>
    /// <param name="typeLigne">Le type de ligne source brut.</param>
    /// <returns><c>true</c> pour le type 5 (frais vendeur).</returns>
    internal static bool IsSellerFeeLine(string? typeLigne) => typeLigne == LigneFraisVendeur;

    /// <summary>
    /// Conversion OBLIGATOIRE flottant legacy → <c>decimal</c> au centime, arrondi commercial
    /// (half-up / away-from-zero) — ADR-0004 D3-7, CLAUDE.md n°1. Les bases Pervasive stockent des
    /// flottants « sales » (p. ex. 8.329999999999998) : la cast double→decimal de .NET arrondit à
    /// 15 chiffres significatifs (nettoyant le bruit binaire), puis l'arrondi au centime fixe l'échelle.
    /// Un flottant NaN, infini ou hors de la plage decimal lève une <see cref="SourceSchemaException"/>
    /// typée (F01-F02 R7) — jamais arrondi à l'aveugle (ADR-0004 D3-7).
    /// </summary>
    /// <param name="raw">Le montant brut (flottant source).</param>
    /// <returns>Le montant en <c>decimal</c> arrondi à 2 décimales (half-up).</returns>
    internal static decimal RoundAmount(double raw)
    {
        if (double.IsNaN(raw) || double.IsInfinity(raw))
        {
            throw new SourceSchemaException(
                $"Montant source illisible (NaN/Infini) : valeur brute « {raw} » reçue. "
                + "Document bloqué, jamais arrondi à l'aveugle (ADR-0004 D3-7). "
                + "Vérifiez l'extraction des montants en source.");
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

    /// <summary>
    /// Conversion gardée d'un flottant non-montant (taux, quantité) en <c>decimal</c> sans arrondi
    /// supplémentaire (ADR-0004 D3-7). Lève une <see cref="SourceSchemaException"/> typée si la valeur
    /// est NaN, infinie ou hors de la plage decimal (F01-F02 R7).
    /// </summary>
    /// <param name="raw">La valeur brute (flottant source).</param>
    /// <param name="field">Le nom du champ source, inclus dans le message opérateur.</param>
    /// <returns>La valeur convertie en <c>decimal</c> sans arrondi.</returns>
    internal static decimal SanitizeNonAmount(double raw, string field)
    {
        if (double.IsNaN(raw) || double.IsInfinity(raw))
        {
            throw new SourceSchemaException(
                $"Valeur source illisible pour le champ « {field} » (NaN/Infini) : "
                + $"valeur brute « {raw} » reçue. Document bloqué (ADR-0004 D3-7). "
                + "Vérifiez l'extraction des données source.");
        }

        try
        {
            return (decimal)raw;
        }
        catch (OverflowException ex)
        {
            throw new SourceSchemaException(
                $"Valeur source hors de la plage decimal pour le champ « {field} » : "
                + $"valeur brute « {raw} » reçue. Document bloqué (ADR-0004 D3-7). "
                + "Vérifiez l'extraction des données source.",
                ex);
        }
    }

    private static string SourceRef(string noBa) => SourceReferencePrefix + noBa;

    private static string PaymentSourceRef(EncheresV6Bordereau bordereau, EncheresV6Ligne reglement)
    {
        if (!string.IsNullOrWhiteSpace(reglement.NoRemise))
        {
            return "no_remise=" + reglement.NoRemise;
        }

        return SourceRef(bordereau.NoBa ?? string.Empty) + "/ligne=" + (reglement.NoLigne ?? string.Empty);
    }

    private static string RequireField(string? value, string fieldName, string? noBa)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new SourceSchemaException(
                $"Champ source obligatoire « {fieldName} » absent (no_ba « {noBa} ») : schéma EncheresV6 "
                + "incompatible. Vérifiez la requête d'extraction.");
        }

        return value!;
    }

    private static PivotLineDto MapLine(EncheresV6Ligne ligne, string noBa)
    {
        string description = RequireField(ligne.Designation, "designation", noBa);

        IReadOnlyList<string> regimeCodes = string.IsNullOrWhiteSpace(ligne.CodeRegime)
            ? Array.Empty<string>()
            : new[] { ligne.CodeRegime! };

        var taxes = new[]
        {
            new PivotLineTaxDto(
                taxAmount: RoundAmount(ligne.MontantTva),
                rate: ligne.TauxTva.HasValue ? SanitizeNonAmount(ligne.TauxTva.Value, "taux_tva") : (decimal?)null,
                categoryCode: null,
                vatexCode: null),
        };

        return new PivotLineDto(
            description: description,
            netAmount: RoundAmount(ligne.MontantHt),
            quantity: ligne.Quantite.HasValue ? SanitizeNonAmount(ligne.Quantite.Value, "quantite") : 1m,
            unitPriceNet: ligne.PrixUnitaire.HasValue ? RoundAmount(ligne.PrixUnitaire.Value) : (decimal?)null,
            sourceRegimeCodes: regimeCodes,
            taxes: taxes,
            sourceLineRef: ligne.NoLigne,
            sourceData: BuildLineSourceData(ligne));
    }

    private static PivotPartyDto MapEmitter(EncheresV6EmitterIdentity emitter)
    {
        PivotAddressDto? address = HasAddress(emitter.Street, emitter.PostalCode, emitter.City, emitter.CountryCode)
            ? new PivotAddressDto(
                line1: emitter.Street,
                postalCode: emitter.PostalCode,
                city: emitter.City,
                countryCode: emitter.CountryCode)
            : null;

        return new PivotPartyDto(
            name: emitter.Name,
            siren: emitter.Siren,
            siret: emitter.Siret,
            vatNumber: emitter.VatNumber,
            address: address,
            isCompanyHint: false);
    }

    private static PivotPartyDto? MapCustomer(EncheresV6Bordereau bordereau)
    {
        // B2C anonyme : un bordereau peut n'avoir aucun acheteur nommé (le e-reporting B2C ne transmet
        // pas de données nominatives) — dans ce cas, pas de tiers destinataire (F01-F02 §6).
        if (string.IsNullOrWhiteSpace(bordereau.AcheteurNom))
        {
            return null;
        }

        PivotAddressDto? address = HasAddress(null, bordereau.AcheteurCodePostal, bordereau.AcheteurVille, bordereau.AcheteurPays)
            ? new PivotAddressDto(
                postalCode: bordereau.AcheteurCodePostal,
                city: bordereau.AcheteurVille,
                countryCode: bordereau.AcheteurPays)
            : null;

        // IsCompanyHint = transcription BRUTE du champ source « societe » non vide — aucune heuristique
        // côté adaptateur (F01-F02 §3.2 amendé) ; toute décision B2B/B2C vit dans Validation (VAL05).
        return new PivotPartyDto(
            name: bordereau.AcheteurNom!,
            siren: bordereau.AcheteurSiren,
            address: address,
            isCompanyHint: !string.IsNullOrWhiteSpace(bordereau.AcheteurSociete));
    }

    private static PivotDocumentRefDto[] MapCreditNoteRefs(
        EncheresV6Bordereau bordereau,
        string kind,
        EncheresV6Bordereau? creditNoteOrigin)
    {
        if (kind != PieceAvoir)
        {
            return Array.Empty<PivotDocumentRefDto>();
        }

        if (creditNoteOrigin is null)
        {
            throw new SourceSchemaException(
                $"Avoir « {bordereau.NumeroPiece} » (no_ba « {bordereau.NoBa} ») sans bordereau d'origine "
                + $"résoluble (no_ba_lettrage « {bordereau.NoBaLettrage} ») : document bloqué, l'origine n'est "
                + "jamais devinée (ADR-0004 D3-3). Vérifiez le lettrage en source.");
        }

        string originNumber = RequireField(creditNoteOrigin.NumeroPiece, "numero_piece (origine avoir)", creditNoteOrigin.NoBa);
        string originNoBa = RequireField(creditNoteOrigin.NoBa, "no_ba (origine avoir)", creditNoteOrigin.NoBa);

        // La date de la facture d'origine (amended_date, EN 16931 BT-25/BG-3) est OBLIGATOIRE et transmise
        // telle quelle (PivotDocumentRefDto.IssueDate non-nullable). Si elle est absente/illisible en source,
        // on BLOQUE l'avoir — jamais une date fabriquée (default 0001-01-01), miroir du garde de la date du
        // document principal ci-dessus (ADR-0004 D3-3, CLAUDE.md n°3 « bloquer plutôt qu'envoyer faux »).
        if (creditNoteOrigin.DateVente == default(DateTime))
        {
            throw new SourceSchemaException(
                $"Avoir « {bordereau.NumeroPiece} » (no_ba « {bordereau.NoBa} ») : date d'émission de la "
                + $"facture d'origine (no_ba « {originNoBa} ») absente ou illisible. La date de référence "
                + "d'avoir (amended_date, BT-25) est obligatoire : document bloqué, jamais devinée "
                + "(ADR-0004 D3-3). Vérifiez le lettrage et la date en source.");
        }

        return new[]
        {
            new PivotDocumentRefDto(
                number: originNumber,
                issueDate: creditNoteOrigin.DateVente,
                sourceReference: SourceRef(originNoBa)),
        };
    }

    private static bool HasAddress(string? line1, string? postalCode, string? city, string? countryCode) =>
        !string.IsNullOrWhiteSpace(line1)
        || !string.IsNullOrWhiteSpace(postalCode)
        || !string.IsNullOrWhiteSpace(city)
        || !string.IsNullOrWhiteSpace(countryCode);

    private static string BuildLineSourceData(EncheresV6Ligne ligne) =>
        JsonConvert.SerializeObject(new SourceDataLine
        {
            MontantHtBrut = ligne.MontantHt,
            MontantTvaBrut = ligne.MontantTva,
            TauxTvaBrut = ligne.TauxTva,
            CodeRegime = ligne.CodeRegime,
        });

    private static string BuildDocumentSourceData(EncheresV6Bordereau bordereau) =>
        JsonConvert.SerializeObject(new SourceDataDocument
        {
            NoBa = bordereau.NoBa,
            TotalHtBrut = bordereau.TotalHt,
            TotalTvaBrut = bordereau.TotalTva,
            TotalTtcBrut = bordereau.TotalTtc,
        });

    // Vue déterministe (ordre de déclaration figé) des montants source ORIGINAUX non arrondis,
    // sérialisée en JSON pour la traçabilité (F01-F02 §3.7 règle 1). L'invariance de l'ordre garantit
    // un JSON canonique — donc une empreinte — stable.
    private sealed class SourceDataLine
    {
        [JsonProperty("montant_ht_brut")]
        public double MontantHtBrut { get; set; }

        [JsonProperty("montant_tva_brut")]
        public double MontantTvaBrut { get; set; }

        [JsonProperty("taux_tva_brut", NullValueHandling = NullValueHandling.Include)]
        public double? TauxTvaBrut { get; set; }

        [JsonProperty("code_regime", NullValueHandling = NullValueHandling.Include)]
        public string? CodeRegime { get; set; }
    }

    private sealed class SourceDataDocument
    {
        [JsonProperty("no_ba", NullValueHandling = NullValueHandling.Include)]
        public string? NoBa { get; set; }

        [JsonProperty("total_ht_brut")]
        public double TotalHtBrut { get; set; }

        [JsonProperty("total_tva_brut")]
        public double TotalTvaBrut { get; set; }

        [JsonProperty("total_ttc_brut")]
        public double TotalTtcBrut { get; set; }
    }
}
