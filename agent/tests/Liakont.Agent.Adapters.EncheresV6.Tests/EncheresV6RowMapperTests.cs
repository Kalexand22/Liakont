namespace Liakont.Agent.Adapters.EncheresV6.Tests;

using System;
using FluentAssertions;
using Liakont.Agent.Adapters.EncheresV6.Source;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Core.Extraction;
using Xunit;

/// <summary>
/// Tests de la transformation BRUTE EncheresV6 → pivot (<see cref="EncheresV6RowMapper"/>), modèle BA/BV
/// de la marge (validé + sourcé). Vérifie les invariants du contrat d'extraction : commission acheteur en
/// LIGNE au rôle <see cref="PivotLineRole.BuyerFee"/> TTC (BA, BUG-17 volet b), commission vendeur en
/// <c>SellerFees</c> TTC (BV, lignes type 2), adjudication + honoraire = lignes du document, émetteur/nature
/// d'opération NON portés (FilledByPlatform),
/// conversion flottant→decimal half-up (CLAUDE.md n°1), aucun mapping TVA (R3), avoir orphelin bloqué.
/// </summary>
public class EncheresV6RowMapperTests
{
    [Theory]
    [InlineData(8.329999999999998, 8.33)] // flottant Pervasive « sale » → nettoyé puis arrondi
    [InlineData(1.666, 1.67)]
    [InlineData(2.675, 2.68)] // demi-centime → half-up (away-from-zero)
    [InlineData(100.0, 100.00)]
    public void RoundAmount_converts_dirty_float_half_up(double raw, decimal expected)
    {
        EncheresV6RowMapper.RoundAmount(raw).Should().Be(expected);
    }

    [Fact]
    public void RoundAmount_throws_schema_exception_on_NaN_infinity_or_overflow()
    {
        ((Action)(() => EncheresV6RowMapper.RoundAmount(double.NaN))).Should().Throw<SourceSchemaException>();
        ((Action)(() => EncheresV6RowMapper.RoundAmount(double.PositiveInfinity))).Should().Throw<SourceSchemaException>();
        ((Action)(() => EncheresV6RowMapper.RoundAmount(1e30))).Should().Throw<SourceSchemaException>();
    }

    [Fact]
    public void MapBaDocument_does_not_carry_supplier_or_operation_category()
    {
        PivotDocumentDto doc = EncheresV6RowMapper.MapBaDocument(MargeBa(), null);

        doc.Supplier.Should().BeNull("l'émetteur est rempli par la plateforme (FilledByPlatform), jamais par l'agent");
        doc.OperationCategory.Should().BeNull("la nature d'opération est plateforme, jamais devinée (CLAUDE.md n°2)");
        doc.SourceDocumentKind.Should().Be("B", "le type de pièce source est transporté brut (ADR-0004 D3-3)");
        doc.SourceReference.Should().Be("encheresv6:ba:100022");
        doc.Number.Should().Be("100022");
    }

    [Fact]
    public void MapBaDocument_carries_buyer_commission_as_ttc_fee_not_a_taxable_line()
    {
        PivotDocumentDto doc = EncheresV6RowMapper.MapBaDocument(MargeBa(), null);

        // BUG-17 volet b : l'honoraire acheteur est désormais porté en LIGNE (rôle BuyerFee), plus dans le
        // side-channel hors-lignes BuyerFees. Le bordereau a donc DEUX lignes : adjudication + honoraire.
        doc.Lines.Should().HaveCount(2);

        // Adjudication = 1re ligne (régime marge → TVA 0 → document marge propre, art. 297 E).
        var adjudication = doc.Lines[0];
        adjudication.Role.Should().Be(PivotLineRole.Standard, "l'adjudication est une ligne ordinaire");
        adjudication.NetAmount.Should().Be(2000.00m);
        adjudication.Taxes[0].TaxAmount.Should().Be(0m, "sous le régime de la marge l'adjudication est exonérée");
        adjudication.Taxes[0].CategoryCode.Should().BeNull("le mapping TVA est plateforme (R3)");
        adjudication.SourceRegimeCodes.Should().ContainSingle().Which.Should().Be("6");
        doc.Totals.TotalTax.Should().Be(0m);

        // Commission acheteur = LIGNE au rôle BuyerFee, NetAmount TTC (HT 334.40 + TVA 66.88), taxe de ligne à 0
        // (l'agent ne CLASSE pas — la catégorie vient du mapping plateforme), même régime que l'adjudication.
        var honoraire = doc.Lines[1];
        honoraire.Role.Should().Be(PivotLineRole.BuyerFee, "l'honoraire acheteur porte le rôle BuyerFee");
        honoraire.NetAmount.Should().Be(401.28m, "NetAmount TTC = 334.40 + 66.88");
        honoraire.Taxes[0].TaxAmount.Should().Be(0m, "la ligne d'honoraire ne porte aucune TVA distincte (art. 297 E)");
        honoraire.Taxes[0].CategoryCode.Should().BeNull("le mapping TVA est plateforme (R3)");
        honoraire.SourceRegimeCodes.Should().ContainSingle().Which.Should().Be("6", "même régime que l'adjudication du lot");

        // TVA de frais SOURCE transportée brute (F03 §2.8, sans logique fiscale) — la plateforme s'en sert pour
        // recouvrer la base HT exonérée d'un export ; ici non nulle (commission taxable), donc portée telle quelle.
        honoraire.SourceTaxAmount.Should().Be(66.88m);

        // Le total du document inclut désormais l'honoraire (le bordereau a un total réel : 2000 + 401.28).
        doc.Totals.TotalNet.Should().Be(2401.28m, "le total inclut l'adjudication ET l'honoraire acheteur porté en ligne");

        doc.BuyerFees.Should().BeNull("l'honoraire acheteur n'est plus porté dans le side-channel BuyerFees (BUG-17 volet b)");
        doc.SellerFees.Should().BeNull("le BA ne porte pas la jambe vendeur");
    }

    [Fact]
    public void MapBaDocument_reads_taxable_adjudication_vat_from_either_column()
    {
        EncheresV6Bordereau ba = MargeBa();
        ba.Lignes[0].CodeRegime = "5"; // taxable
        ba.Lignes[0].MttTvaInclusAdj = 400.00; // TVA d'adjudication « incluse »
        ba.Lignes[0].MttTvaEnPlusAdj = 0.0;

        PivotDocumentDto doc = EncheresV6RowMapper.MapBaDocument(ba, null);

        doc.Lines[0].Taxes[0].TaxAmount.Should().Be(400.00m);
        doc.Totals.TotalTax.Should().Be(400.00m);
    }

    [Fact]
    public void MapBaDocument_non_export_keeps_the_bare_regime_key()
    {
        // code_export=0 (cas nominal) → clé de régime NUE, zéro régression sur le domestique (F03 §2.8).
        EncheresV6Bordereau domestique = MargeBa();
        domestique.Lignes[0].CodeRegime = "5";
        domestique.CodeExport = false;

        PivotDocumentDto doc = EncheresV6RowMapper.MapBaDocument(domestique, null);

        doc.Lines[0].SourceRegimeCodes.Should().ContainSingle().Which.Should().Be("5");
    }

    [Theory]
    [InlineData("HORS CEE", "EXP_HORSUE")] // export hors UE → mappé G/0 % (262 I)
    [InlineData("CEE", "EXP_CEE")] // intra-UE → mappé K/0 % (262 ter / 258 A)
    [InlineData("FRANCE", "EXP_FR")] // franchise (mode FRANCE + code_export, art. 275) → G/0 %
    [InlineData("", "EXP_FR")] // mode absent → zone par défaut FR
    public void MapBaDocument_export_emits_zone_regime_key(string modeLivraison, string expectedRegime)
    {
        // F03 §2.8 : code_export=1 → clé de régime par ZONE « EXP_{zone} » (RegimeKeyShape.Composite). Le régime
        // domestique ne figure PAS dans la clé (l'exonération internationale prime — 262 I/262 ter/275) ; il reste
        // dans SourceData. Transport de donnée source, AUCUNE dérivation fiscale (CLAUDE.md n°6).
        EncheresV6Bordereau export = MargeBa();
        export.Lignes[0].CodeRegime = "5"; // assujetti (prix total) MAIS exporté
        export.CodeExport = true;
        export.ModeLivraison = modeLivraison;

        PivotDocumentDto doc = EncheresV6RowMapper.MapBaDocument(export, null);

        doc.Lines[0].SourceRegimeCodes.Should().ContainSingle().Which.Should().Be(expectedRegime);
    }

    [Fact]
    public void MapBaDocument_normalizes_source_currency_label_EURO_to_iso_EUR()
    {
        // La source EncheresV6 étiquette l'euro « EURO » (libellé non-ISO) : l'agent le normalise vers l'ISO 4217
        // « EUR » (sinon la plateforme BLOQUE le document — code devise invalide). Normalisation de FORMAT, pas fiscale.
        EncheresV6Bordereau euro = MargeBa();
        euro.CodeDevise = "EURO";
        EncheresV6RowMapper.MapBaDocument(euro, null).CurrencyCode.Should().Be("EUR");

        EncheresV6Bordereau vide = MargeBa();
        vide.CodeDevise = string.Empty;
        EncheresV6RowMapper.MapBaDocument(vide, null).CurrencyCode.Should().Be("EUR", "devise absente → EUR domestique");

        EncheresV6Bordereau iso = MargeBa();
        iso.CodeDevise = "USD";
        EncheresV6RowMapper.MapBaDocument(iso, null).CurrencyCode.Should().Be("USD", "un code ISO valide est transporté tel quel");
    }

    [Fact]
    public void MapBaDocument_normalizes_non_iso_country_code_UK_nations_to_GB()
    {
        // La source EncheresV6 étiquette certaines nations du Royaume-Uni par un code non-ISO (« ENG »/« SCO »/
        // « WAL »/« NIR » — codes de subdivision ISO 3166-2, pas des pays alpha-2) : l'agent les normalise vers
        // « GB » (ISO 3166-1, sinon la plateforme BLOQUE — code pays invalide). Donnée legacy, pas fiscale.
        EncheresV6Bordereau eng = MargeBa();
        eng.CodePays = "ENG";
        EncheresV6RowMapper.MapBaDocument(eng, null).Customer!.Address!.CountryCode.Should().Be("GB");

        EncheresV6Bordereau sco = MargeBa();
        sco.CodePays = " sco ";
        EncheresV6RowMapper.MapBaDocument(sco, null).Customer!.Address!.CountryCode.Should().Be("GB", "casse et espaces tolérés");

        EncheresV6Bordereau isoPays = MargeBa();
        isoPays.CodePays = "FR";
        EncheresV6RowMapper.MapBaDocument(isoPays, null).Customer!.Address!.CountryCode.Should().Be("FR", "un code ISO valide est transporté tel quel");

        EncheresV6Bordereau inconnu = MargeBa();
        inconnu.CodePays = "ZZ";
        EncheresV6RowMapper.MapBaDocument(inconnu, null).Customer!.Address!.CountryCode.Should().Be("ZZ", "un code non listé reste strictement brut — l'adaptateur ne devine jamais un pays");
    }

    [Fact]
    public void MapBaDocument_sets_company_hint_and_siren_raw_without_heuristic()
    {
        PivotDocumentDto particulier = EncheresV6RowMapper.MapBaDocument(MargeBa(), null);
        particulier.Customer!.IsCompanyHint.Should().BeFalse();
        particulier.Customer.Siren.Should().BeNull();

        EncheresV6Bordereau pro = MargeBa();
        pro.Societe = "AUTOSUD21";
        pro.AcheteurSiren = "404482572";
        PivotDocumentDto professionnel = EncheresV6RowMapper.MapBaDocument(pro, null);
        professionnel.Customer!.IsCompanyHint.Should().BeTrue("le champ societe non vide est un indice brut");
        professionnel.Customer.Siren.Should().Be("404482572", "le SIREN est transporté brut, jamais déduit");
    }

    [Fact]
    public void MapBaDocument_orphan_credit_note_is_blocked_never_guessed()
    {
        EncheresV6Bordereau avoir = MargeBa();
        avoir.BordereauOuAvoir = "A";
        avoir.NoBaLettrage = "9999";

        Action act = () => EncheresV6RowMapper.MapBaDocument(avoir, null);

        act.Should().Throw<SourceSchemaException>("un avoir sans origine résoluble est bloqué (ADR-0004 D3-3)");
    }

    [Fact]
    public void MapBaDocument_credit_note_links_to_resolved_origin()
    {
        EncheresV6Bordereau origin = MargeBa();
        origin.NoBa = "100020";
        EncheresV6Bordereau avoir = MargeBa();
        avoir.NoBa = "100099";
        avoir.BordereauOuAvoir = "A";
        avoir.NoBaLettrage = "100020";

        PivotDocumentDto doc = EncheresV6RowMapper.MapBaDocument(avoir, origin);

        doc.SourceDocumentKind.Should().Be("A");
        doc.CreditNoteRefs.Should().ContainSingle();
        doc.CreditNoteRefs[0].Number.Should().Be("100020");
        doc.CreditNoteRefs[0].SourceReference.Should().Be("encheresv6:ba:100020");
    }

    [Fact]
    public void MapBaDocument_throws_when_date_vente_missing()
    {
        EncheresV6Bordereau ba = MargeBa();
        ba.DateVente = default;

        ((Action)(() => EncheresV6RowMapper.MapBaDocument(ba, null))).Should().Throw<SourceSchemaException>();
    }

    [Fact]
    public void MapBvDocument_carries_seller_commission_as_ttc_fee()
    {
        PivotDocumentDto doc = EncheresV6RowMapper.MapBvDocument(MargeBv(), null);

        doc.Supplier.Should().BeNull();
        doc.Number.Should().Be("100012");
        doc.SourceReference.Should().Be("encheresv6:bv:100012");
        doc.Totals.TotalTax.Should().Be(0m, "la jambe vendeur ne porte pas de TVA distincte (marge)");

        // Commission vendeur = SellerFee TTC (HT 300 + TVA 60). Le débours (type 3) n'est PAS porté.
        doc.SellerFees.Should().ContainSingle();
        doc.SellerFees![0].NetAmount.Should().Be(360.00m);
        doc.SellerFees[0].LotReference.Should().Be("100012", "la jambe vendeur est au grain bordereau (no_bv)");
        doc.BuyerFees.Should().BeNull("le BV ne porte pas la jambe acheteur");
    }

    [Fact]
    public void MapBvDocument_excludes_debours_type3_from_margin()
    {
        EncheresV6BordereauVendeur bv = MargeBv();
        bv.Lignes.Add(new EncheresV6LigneVendeur
        {
            TypeLigne = "3", // débours (transport) — HORS marge
            Designation = "Transport",
            MttFraisHt = 100.00,
            MttTvaFrais = 20.00,
        });

        PivotDocumentDto doc = EncheresV6RowMapper.MapBvDocument(bv, null);

        doc.SellerFees.Should().ContainSingle("seule la commission (type 2) est portée ; le débours type 3 est hors marge");
        doc.SellerFees![0].NetAmount.Should().Be(360.00m);
    }

    [Fact]
    public void MapBvDocument_orphan_credit_note_is_blocked_never_guessed()
    {
        EncheresV6BordereauVendeur avoir = MargeBv();
        avoir.BordereauOuAvoir = "A";
        avoir.NoBvLettrage = "9999";

        Action act = () => EncheresV6RowMapper.MapBvDocument(avoir, null);

        act.Should().Throw<SourceSchemaException>("un avoir vendeur sans origine résoluble est bloqué (ADR-0004 D3-3)");
    }

    [Fact]
    public void MapPayment_maps_type3_line_to_raw_pivot_payment()
    {
        var bordereau = new EncheresV6Bordereau { NoBa = "100022" };
        var reglement = new EncheresV6Ligne
        {
            TypeLigne = "3",
            CodeLigne = "CB",
            Designation = "Carte bancaire",
            MontantLigne = 401.28,
            DateReglement = new DateTime(2024, 1, 15),
            NoRemise = "REM-0500",
            NoLignePv = "0",
        };

        PivotPaymentDto payment = EncheresV6RowMapper.MapPayment(bordereau, reglement);

        payment.PaymentDate.Should().Be(new DateTime(2024, 1, 15));
        payment.Amount.Should().Be(401.28m);
        payment.RelatedDocumentNumber.Should().Be("100022");
        payment.SourceReference.Should().Be("encheresv6:remise:REM-0500");
    }

    [Fact]
    public void MapFactureClientDocument_does_not_carry_emitter_nature_or_fees()
    {
        PivotDocumentDto doc = EncheresV6RowMapper.MapFactureClientDocument(StandardFacture(), null);

        doc.Supplier.Should().BeNull("l'émetteur est rempli par la plateforme (FilledByPlatform)");
        doc.OperationCategory.Should().BeNull("la nature TLB1/TPS1 est plateforme (profil tenant), jamais devinée par l'agent (n°6)");
        doc.BuyerFees.Should().BeNull("une facture ordinaire ne porte AUCUN frais d'enchères (discriminant document ordinaire)");
        doc.SellerFees.Should().BeNull();
        doc.SourceDocumentKind.Should().Be("F", "le type de pièce est transporté brut (ADR-0004 D3-3)");
        doc.SourceReference.Should().Be("encheresv6:fc:FAC-001");
        doc.Number.Should().Be("FAC-001");
        doc.CurrencyCode.Should().Be("EUR", "« EURO » source normalisé vers l'ISO 4217");
    }

    [Fact]
    public void MapFactureClientDocument_maps_billed_lines_at_total_price_with_source_vat()
    {
        PivotDocumentDto doc = EncheresV6RowMapper.MapFactureClientDocument(StandardFacture(), null);

        // Seules les 2 lignes FACTURÉES (type 1) sont portées : la ligne de commentaire (TXT, qte/prix nuls) et
        // le règlement (type 2) sont écartés.
        doc.Lines.Should().HaveCount(2);
        doc.Lines[0].NetAmount.Should().Be(100.00m);
        doc.Lines[0].Taxes[0].TaxAmount.Should().Be(20.00m, "TVA ligne = HT × taux_tva, comme la source la calcule (transport, pas une règle)");
        doc.Lines[0].Taxes[0].Rate.Should().BeNull("le taux validé est posé par la plateforme (R3)");
        doc.Lines[0].Taxes[0].CategoryCode.Should().BeNull("le mapping TVA est plateforme");
        doc.Lines[0].SourceRegimeCodes.Should().ContainSingle().Which.Should().Be("20", "clé de régime = taux effectif (taux_tva), pas code_tva (non fiable)");
        doc.Lines[1].NetAmount.Should().Be(144.00m);
        doc.Lines[1].Taxes[0].TaxAmount.Should().Be(28.80m);

        doc.Totals.TotalNet.Should().Be(244.00m);
        doc.Totals.TotalTax.Should().Be(48.80m);
        doc.Totals.TotalGross.Should().Be(292.80m);
        doc.Totals.SourceTotalGross.Should().Be(292.80m, "le TTC d'entête source est porté en contrôle");
    }

    [Fact]
    public void MapFactureClientDocument_reduced_rate_line_uses_its_own_source_rate()
    {
        EncheresV6FactureClient f = StandardFacture();
        f.Lignes[1].CodeTva = 2;
        f.Lignes[1].Qte = 1;
        f.Lignes[1].PrixUnitaireHt = 200.00;
        f.Lignes[1].TauxTva = 5.5;

        PivotDocumentDto doc = EncheresV6RowMapper.MapFactureClientDocument(f, null);

        doc.Lines[1].NetAmount.Should().Be(200.00m);
        doc.Lines[1].Taxes[0].TaxAmount.Should().Be(11.00m, "5,5 % de 200");
        doc.Lines[1].SourceRegimeCodes.Should().ContainSingle().Which.Should().Be("5.5", "le taux 5,5 % donne le jeton « 5.5 » sans erreur d'arrondi (formaté depuis taux_tva, pas recouvré de la TVA arrondie)");
    }

    [Fact]
    public void MapFactureClientDocument_keys_on_rate_not_unreliable_code_tva()
    {
        // Cas réel (facture 00100007) : code_tva=0 MAIS taux 20 % et TVA 28,80 → la clé doit suivre le TAUX (« 20 »),
        // jamais code_tva (« 0 », qui mapperait exonéré et bloquerait à tort une vente taxable à 20 %).
        EncheresV6FactureClient f = StandardFacture();
        f.Lignes[0].CodeTva = 0; // code_tva « ment »
        f.Lignes[0].TauxTva = 20.0;

        PivotDocumentDto doc = EncheresV6RowMapper.MapFactureClientDocument(f, null);

        doc.Lines[0].Taxes[0].TaxAmount.Should().Be(20.00m, "20 % de 100, piloté par le taux source");
        doc.Lines[0].SourceRegimeCodes.Should().ContainSingle().Which.Should().Be("20", "la clé suit le taux réel, pas le code_tva non fiable");
    }

    [Fact]
    public void MapFactureClientDocument_customer_is_b2c_without_siren()
    {
        PivotDocumentDto doc = EncheresV6RowMapper.MapFactureClientDocument(StandardFacture(), null);

        doc.Customer!.Name.Should().Be("Client Particulier");
        doc.Customer.Siren.Should().BeNull("la facture client ne porte pas de SIREN → B2C par construction");
        doc.Customer.IsCompanyHint.Should().BeFalse();
        doc.Customer.Address!.PostalCode.Should().Be("75009");
    }

    [Fact]
    public void MapFactureClientDocument_orphan_credit_note_is_blocked_never_guessed()
    {
        EncheresV6FactureClient avoir = StandardFacture();
        avoir.FactureOuAvoir = "A";
        avoir.NoFactureLettrage = "INCONNUE";

        ((Action)(() => EncheresV6RowMapper.MapFactureClientDocument(avoir, null)))
            .Should().Throw<SourceSchemaException>("un avoir sans facture d'origine résoluble est bloqué (ADR-0004 D3-3)");
    }

    [Fact]
    public void MapFactureClientDocument_credit_note_links_to_resolved_origin()
    {
        EncheresV6FactureClient origin = StandardFacture();
        origin.NoFact = "FAC-000";
        EncheresV6FactureClient avoir = StandardFacture();
        avoir.NoFact = "AV-001";
        avoir.FactureOuAvoir = "A";
        avoir.NoFactureLettrage = "FAC-000";

        PivotDocumentDto doc = EncheresV6RowMapper.MapFactureClientDocument(avoir, origin);

        doc.SourceDocumentKind.Should().Be("A");
        doc.CreditNoteRefs.Should().ContainSingle();
        doc.CreditNoteRefs[0].Number.Should().Be("FAC-000");
        doc.CreditNoteRefs[0].SourceReference.Should().Be("encheresv6:fc:FAC-000");
    }

    [Theory]
    [InlineData(100.00, 20.00, "20")] // 20 %
    [InlineData(200.00, 11.00, "5.5")] // 5,5 %
    [InlineData(72.51, 14.50, "20")] // 19,997 % → arrondi 20,00
    [InlineData(600.00, 0.00, "0")] // frais sans TVA → exonéré
    [InlineData(0.00, 0.00, "0")] // base nulle → taux non recouvrable
    public void RecoverRateToken_reconstructs_effective_source_rate(decimal net, decimal tax, string expected)
    {
        // La note ne porte pas de code_tva : la clé de régime est le TAUX effectif recouvré (TVA/HT), arithmétique
        // de transport sur deux champs source ; la plateforme tranche ensuite la catégorie (arbitrage PO).
        EncheresV6RowMapper.RecoverRateToken(net, tax).Should().Be(expected);
    }

    [Fact]
    public void MapNoteHonoDocument_does_not_carry_emitter_nature_or_fees()
    {
        PivotDocumentDto doc = EncheresV6RowMapper.MapNoteHonoDocument(StandardNote(), null);

        doc.Supplier.Should().BeNull("l'émetteur est rempli par la plateforme (FilledByPlatform)");
        doc.OperationCategory.Should().BeNull("la nature (TPS1 prestation de services) est plateforme, jamais devinée par l'agent (n°6)");
        doc.BuyerFees.Should().BeNull("une note d'honoraires ne porte AUCUN frais d'enchères (document ordinaire)");
        doc.SellerFees.Should().BeNull();
        doc.SourceDocumentKind.Should().Be("F");
        doc.SourceReference.Should().Be("encheresv6:nh:NH-001");
        doc.Number.Should().Be("NH-001");
    }

    [Fact]
    public void MapNoteHonoDocument_maps_honoraires_and_frais_with_distinct_source_vat_and_recovered_rate()
    {
        PivotDocumentDto doc = EncheresV6RowMapper.MapNoteHonoDocument(StandardNote(), null);

        // Honoraires (type 1) + frais (type 2) sont les lignes facturées ; le règlement (type 3) est exclu.
        doc.Lines.Should().HaveCount(2);
        doc.Lines[0].NetAmount.Should().Be(100.00m);
        doc.Lines[0].Taxes[0].TaxAmount.Should().Be(20.00m, "TVA distincte de la ligne (montant_tva), reprise telle quelle (ADR-0015)");
        doc.Lines[0].Taxes[0].Rate.Should().BeNull("le taux validé est posé par la plateforme (R3)");
        doc.Lines[0].SourceRegimeCodes.Should().ContainSingle().Which.Should().Be("20", "taux effectif recouvré, faute de code_tva en source");
        doc.Lines[1].NetAmount.Should().Be(50.00m);
        doc.Lines[1].Taxes[0].TaxAmount.Should().Be(10.00m);
        doc.Lines[1].SourceRegimeCodes.Should().ContainSingle().Which.Should().Be("20");

        doc.Totals.TotalNet.Should().Be(150.00m);
        doc.Totals.TotalTax.Should().Be(30.00m);
        doc.Totals.TotalGross.Should().Be(180.00m);
        doc.Totals.SourceTotalGross.Should().Be(180.00m, "le TTC d'entête source est porté en contrôle");
    }

    [Fact]
    public void MapNoteHonoDocument_zero_rate_fee_recovers_exempt_key()
    {
        // Un frais à 0 % (frais horaires, affranchissement…) recouvre la clé « 0 » → la plateforme la mappe en
        // exonéré, ce qui rend une note à taux MIXTES non marquée (fail-closed, F03 §2.9).
        EncheresV6NoteHono note = StandardNote();
        note.Lignes[1].MontantHt = 600.00;
        note.Lignes[1].MontantTva = 0.00;

        PivotDocumentDto doc = EncheresV6RowMapper.MapNoteHonoDocument(note, null);

        doc.Lines[1].SourceRegimeCodes.Should().ContainSingle().Which.Should().Be("0");
    }

    [Fact]
    public void MapNoteHonoDocument_customer_is_b2c_without_siren()
    {
        PivotDocumentDto doc = EncheresV6RowMapper.MapNoteHonoDocument(StandardNote(), null);

        doc.Customer!.Name.Should().Be("GLOUX");
        doc.Customer.Siren.Should().BeNull("la note ne porte pas de SIREN → B2C par construction (le pro est traité par la plateforme)");
        doc.Customer.IsCompanyHint.Should().BeFalse();
        doc.Customer.Address!.PostalCode.Should().Be("56000");
    }

    [Fact]
    public void MapNoteHonoDocument_orphan_credit_note_is_blocked_never_guessed()
    {
        EncheresV6NoteHono avoir = StandardNote();
        avoir.FactureOuAvoir = "A";
        avoir.NoNoteLettrage = "INCONNUE";

        ((Action)(() => EncheresV6RowMapper.MapNoteHonoDocument(avoir, null)))
            .Should().Throw<SourceSchemaException>("un avoir de note sans note d'origine résoluble est bloqué (ADR-0004 D3-3)");
    }

    [Fact]
    public void MapNoteHonoDocument_credit_note_links_to_resolved_origin()
    {
        EncheresV6NoteHono origin = StandardNote();
        origin.NoNoteHono = "NH-000";
        EncheresV6NoteHono avoir = StandardNote();
        avoir.NoNoteHono = "AV-NH-001";
        avoir.FactureOuAvoir = "A";
        avoir.NoNoteLettrage = "NH-000";

        PivotDocumentDto doc = EncheresV6RowMapper.MapNoteHonoDocument(avoir, origin);

        doc.SourceDocumentKind.Should().Be("A");
        doc.CreditNoteRefs.Should().ContainSingle();
        doc.CreditNoteRefs[0].Number.Should().Be("NH-000");
        doc.CreditNoteRefs[0].SourceReference.Should().Be("encheresv6:nh:NH-000");
    }

    // Note d'honoraires d'inventaire (prestation de services) : honoraires (type 1, 100 @20 %) + frais (type 2,
    // déplacement 50 @20 %) + règlement (type 3, exclu). HT 150 / TVA 30 / TTC 180. Toutes lignes à 20 %.
    private static EncheresV6NoteHono StandardNote()
    {
        var n = new EncheresV6NoteHono
        {
            NoNoteHono = "NH-001",
            FactureOuAvoir = "F",
            DateFacture = new DateTime(2026, 1, 20),
            Nom = "GLOUX",
            Adresse = "1 rue de la Criée",
            CodePostal = "56000",
            Ville = "Vannes",
            CodePays = "FR",
            MontantTtc = 180.00,
            CodeDevise = "EURO",
        };

        n.Lignes.Add(new EncheresV6NoteHonoLigne { TypeLigne = "1", CodeLigne = string.Empty, Libelle = "Honoraires d'inventaire", MontantHt = 100.00, MontantTva = 20.00 });
        n.Lignes.Add(new EncheresV6NoteHonoLigne { TypeLigne = "2", CodeLigne = "2", Libelle = "Déplacement", MontantHt = 50.00, MontantTva = 10.00 });
        n.Lignes.Add(new EncheresV6NoteHonoLigne { TypeLigne = "3", CodeLigne = "CE", Libelle = "Chèque", MontantHt = 180.00, MontantTva = 0.00 });

        return n;
    }

    // Facture client ORDINAIRE (hors enchères) : 2 lignes facturées (HONO 100 @20 %, caisse de vins 12×12 @20 %)
    // + 1 ligne de commentaire (TXT, écartée) + 1 règlement (type 2, écarté). HT 244 / TVA 48,80 / TTC 292,80.
    private static EncheresV6FactureClient StandardFacture()
    {
        var f = new EncheresV6FactureClient
        {
            NoFact = "FAC-001",
            FactureOuAvoir = "F",
            DateFact = new DateTime(2026, 1, 20),
            Nom = "Client",
            Prenom = "Particulier",
            Adresse1 = "11 rue des Prunes",
            Cp = "75009",
            Ville = "Paris",
            CodePays = "FR",
            MontantHt = 244.00,
            MontantTva = 48.80,
            MontantTtc = 292.80,
            CodeDevise = "EURO",
        };

        f.Lignes.Add(new EncheresV6FactureClientLigne { TypeLigne = "1", NoLigne = "1", CodeArticle = "HONO", Designation = "Honoraires inventaire", Qte = 1, PrixUnitaireHt = 100.00, CodeTva = 1, TauxTva = 20.0 });
        f.Lignes.Add(new EncheresV6FactureClientLigne { TypeLigne = "1", NoLigne = "2", CodeArticle = "CV", Designation = "Caisse de vins", Qte = 12, PrixUnitaireHt = 12.00, CodeTva = 1, TauxTva = 20.0 });
        f.Lignes.Add(new EncheresV6FactureClientLigne { TypeLigne = "1", NoLigne = "3", CodeArticle = "TXT", Designation = "Merci de votre confiance", Qte = 0, PrixUnitaireHt = 0.0, CodeTva = 0, TauxTva = 0.0 });
        f.Lignes.Add(new EncheresV6FactureClientLigne { TypeLigne = "2", NoLigne = "1", CodeArticle = string.Empty, Designation = "Chèque", Qte = 0, PrixUnitaireHt = 292.80, CodeTva = 0, TauxTva = 0.0 });

        return f;
    }

    // Bordereau ACHETEUR : 1 lot au régime 6 (marge), adjudication 2000 (TVA 0) + commission acheteur
    // 334.40 HT + 66.88 TVA = 401.28 TTC (cas réel no_ba=100022).
    private static EncheresV6Bordereau MargeBa()
    {
        var ba = new EncheresV6Bordereau
        {
            NoBa = "100022",
            BordereauOuAvoir = "B",
            DateVente = new DateTime(2024, 1, 12),
            Nom = "Acheteur Particulier (fictif)",
            CodePostal = "35000",
            Ville = "Rennes",
            CodePays = "FR",
            TotalBordereau = 2401.28,
        };

        ba.Lignes.Add(new EncheresV6Ligne
        {
            TypeLigne = "1",
            NoLignePv = "1",
            Designation = "Adjudication lot 1",
            MontantAdjHt = 2000.00,
            MttTvaInclusAdj = 0.0,
            MttTvaEnPlusAdj = 0.0,
            MontantFraisHt = 334.40,
            MontantTvaFrais = 66.88,
            CodeRegime = "6",
        });

        return ba;
    }

    // Bordereau VENDEUR : 1 lot (adjudication) + commission vendeur 300 HT + 60 TVA = 360 TTC (type 2).
    private static EncheresV6BordereauVendeur MargeBv()
    {
        var bv = new EncheresV6BordereauVendeur
        {
            NoBv = "100012",
            BordereauOuAvoir = "B",
            DateVente = new DateTime(2024, 1, 12),
            Nom = "Vendeur Commettant (fictif)",
            CodeRegimeTva = "6",
            TotalBordereau = 1860.00,
        };

        bv.Lignes.Add(new EncheresV6LigneVendeur
        {
            TypeLigne = "1",
            NoLignePv = "1",
            Designation = "Adjudication lot 1",
            MontantAdjHt = 2000.00,
        });
        bv.Lignes.Add(new EncheresV6LigneVendeur
        {
            TypeLigne = "2",
            NoLignePv = "0",
            Designation = "Frais de vente 15%",
            MttFraisHt = 300.00,
            MttTvaFrais = 60.00,
        });

        return bv;
    }
}
