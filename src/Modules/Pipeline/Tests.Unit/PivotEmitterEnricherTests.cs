namespace Liakont.Modules.Pipeline.Tests.Unit;

using System;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Pipeline.Infrastructure;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Xunit;

/// <summary>
/// Remplissage de l'émetteur au READ-TIME (CHECK/SEND) depuis le profil tenant (ADR-0031 amendé / RB9) :
/// l'agent ne porte plus l'identité émetteur, la plateforme la remplit au traitement (PAS à l'ingestion —
/// l'anti-doublon F06 hashe le pivot SOURCE). Garde les invariants fiscaux : remplissage QUAND ABSENT (un
/// émetteur déjà porté — 389 — n'est pas écrasé), profil incomplet → champ laissé nul (bloqué au CHECK,
/// jamais deviné — CLAUDE.md n°2/n°3), nature d'opération parsée PAR NOM (les deux enums OperationCategory
/// ont des valeurs numériques différentes).
/// </summary>
public class PivotEmitterEnricherTests
{
    [Fact]
    public void Fills_supplier_and_operation_category_from_tenant_profile_when_absent()
    {
        PivotDocumentDto enriched = PivotEmitterEnricher.Enrich(
            Pivot(supplier: null, operationCategory: null),
            Profile("123456782", "SEM Keroman"),
            Fiscal("LivraisonBiens"));

        enriched.Supplier.Should().NotBeNull();
        enriched.Supplier!.Siren.Should().Be("123456782");
        enriched.Supplier.Name.Should().Be("SEM Keroman");
        enriched.Supplier.IsCompanyHint.Should().BeTrue();
        enriched.Supplier.Address!.City.Should().Be("Lorient");
        enriched.OperationCategory.Should().Be(OperationCategory.LivraisonBiens);
    }

    [Fact]
    public void Leaves_emitter_null_when_profile_is_absent_so_check_blocks()
    {
        PivotDocumentDto enriched = PivotEmitterEnricher.Enrich(
            Pivot(supplier: null, operationCategory: null), profile: null, fiscal: null);

        enriched.Supplier.Should().BeNull("profil non configuré → bloqué au CHECK, jamais inventé");
        enriched.OperationCategory.Should().BeNull();
    }

    [Fact]
    public void Leaves_supplier_null_when_profile_siren_is_blank()
    {
        PivotDocumentDto enriched = PivotEmitterEnricher.Enrich(
            Pivot(supplier: null, operationCategory: null), Profile(string.Empty, "X"), Fiscal("Mixte"));

        enriched.Supplier.Should().BeNull();
        enriched.OperationCategory.Should().Be(OperationCategory.Mixte);
    }

    [Fact]
    public void Does_not_overwrite_an_emitter_already_carried_by_the_document()
    {
        // 389 (autofacturation sous mandat) : le vendeur est le MANDANT, porté par la source — jamais écrasé.
        var existing = new PivotPartyDto(name: "Mandant", siren: "999999999");

        PivotDocumentDto enriched = PivotEmitterEnricher.Enrich(
            Pivot(supplier: existing, operationCategory: OperationCategory.PrestationServices),
            Profile("123456782", "SEM Keroman"),
            Fiscal("LivraisonBiens"));

        enriched.Supplier!.Siren.Should().Be("999999999");
        enriched.OperationCategory.Should().Be(OperationCategory.PrestationServices);
    }

    [Fact]
    public void Operation_category_with_unknown_name_is_left_null()
    {
        PivotDocumentDto enriched = PivotEmitterEnricher.Enrich(
            Pivot(supplier: null, operationCategory: null), Profile("123456782", "X"), Fiscal("PasUneNature"));

        enriched.OperationCategory.Should().BeNull("nature d'opération inconnue → bloquée au CHECK, jamais devinée");
    }

    [Fact]
    public void Derives_french_intracom_vat_BT31_from_a_well_formed_french_siren()
    {
        // BT-31 dérivé du SIREN émetteur (EN 16931) : « FR » + clé de contrôle + SIREN. La clé est la formule
        // administrative STANDARD française (12 + 3 × (SIREN mod 97)) mod 97 — déterministe, jamais inventée
        // (CLAUDE.md n°2). Requise par la conversion EN 16931 dès qu'une ligne porte de la TVA (BR-S-02).
        PivotDocumentDto enriched = PivotEmitterEnricher.Enrich(
            Pivot(supplier: null, operationCategory: null),
            Profile("123456782", "SEM Keroman"),
            Fiscal("LivraisonBiens"));

        // (12 + 3 × (123456782 mod 97)) mod 97 = 11 → FR11123456782.
        enriched.Supplier!.VatNumber.Should().Be("FR11123456782");

        // Forme : « FR » + clé (2 chiffres) + SIREN (9 chiffres) = 13 caractères, clé cohérente avec le SIREN
        // (même formule que le validateur F04 §4.2 ; le module Validation n'est pas référencé ici, on revérifie).
        var vat = enriched.Supplier.VatNumber!;
        vat.Should().HaveLength(13).And.StartWith("FR");
        var siren = int.Parse(vat[4..], System.Globalization.CultureInfo.InvariantCulture);
        var key = int.Parse(vat[2..4], System.Globalization.CultureInfo.InvariantCulture);
        key.Should().Be((12 + (3 * (siren % 97))) % 97, "la clé de contrôle est cohérente avec le SIREN intégré");
    }

    [Fact]
    public void Does_not_derive_vat_when_country_is_not_france()
    {
        PivotDocumentDto enriched = PivotEmitterEnricher.Enrich(
            Pivot(supplier: null, operationCategory: null),
            ProfileWithCountry("123456782", "Acme GmbH", "DE"),
            Fiscal("LivraisonBiens"));

        // BT-31 dérivé UNIQUEMENT pour un émetteur français : un autre pays → null (jamais une clé FR sur un
        // SIREN non français — la dérivation n'est sourcée que pour la France, CLAUDE.md n°2).
        enriched.Supplier!.Siren.Should().Be("123456782", "le SIREN reste posé, seule la dérivation BT-31 est conditionnée au pays");
        enriched.Supplier.VatNumber.Should().BeNull("n° TVA intracom FR dérivé seulement pour country == FR");
    }

    [Theory]
    [InlineData("12345678")] // 8 chiffres (longueur != 9)
    [InlineData("1234567890")] // 10 chiffres (longueur != 9)
    [InlineData("12345678A")] // 9 caractères mais non numérique
    public void Does_not_derive_vat_when_siren_is_malformed(string malformedSiren)
    {
        PivotDocumentDto enriched = PivotEmitterEnricher.Enrich(
            Pivot(supplier: null, operationCategory: null),
            Profile(malformedSiren, "SEM Keroman"),
            Fiscal("LivraisonBiens"));

        // SIREN mal formé (longueur ≠ 9 ou non numérique) : BT-31 laissé null (jamais deviné). Le SIREN
        // non vide est tout de même posé sur l'émetteur — c'est le CHECK qui le validera/bloquera.
        enriched.Supplier!.VatNumber.Should().BeNull("BT-31 dérivé seulement d'un SIREN bien formé (9 chiffres)");
    }

    [Fact]
    public void Rebuild_preserves_every_additive_pivot_field_when_filling_the_emitter()
    {
        // Régression : l'enrichissement émetteur reconstruit le pivot champ par champ (Rebuild). TOUT champ
        // ADDITIF de fin de contrat (BT-9 échéance, marqueur 10.3, frais vendeur/acheteur, BG-14 période) doit
        // SURVIVRE — un oubli au Rebuild les droppe silencieusement au CHECK/SEND (le merge ADR-0007 avait
        // laissé tomber InvoicePeriod ici ; ce test garde les cinq).
        var document = new PivotDocumentDto(
            sourceDocumentKind: "FAC",
            number: "A-2026-0002",
            issueDate: new DateTime(2026, 6, 1),
            sourceReference: "demoerpa:A-2026-0002",
            supplier: null,
            totals: new PivotTotalsDto(100m, 20m, 120m),
            operationCategory: null,
            paymentDueDate: new DateTime(2026, 7, 1),
            isB2cReportingDeclaration: true,
            sellerFees: new[]
            {
                new PivotSellerFeeDto(
                    lotReference: "no_ba=42", netAmount: 50.00m, sourceRegimeCode: "MARGE",
                    sourceLineRef: "ligne#bv", description: "Frais vendeur fictif"),
            },
            buyerFees: new[]
            {
                new PivotBuyerFeeDto(
                    lotReference: "no_ba=42", netAmount: 30.00m, sourceRegimeCode: "MARGE",
                    sourceLineRef: "ligne#ba", description: "Frais acheteur fictif"),
            },
            invoicePeriod: new PivotInvoicePeriodDto(new DateTime(2026, 6, 1), new DateTime(2026, 6, 30)));

        // Émetteur absent + profil fourni → Enrich reconstruit le pivot (Rebuild).
        PivotDocumentDto enriched = PivotEmitterEnricher.Enrich(
            document, Profile("123456782", "SEM Keroman"), Fiscal("LivraisonBiens"));

        enriched.Supplier.Should().NotBeNull("l'émetteur a été rempli → le pivot a bien été reconstruit");
        enriched.PaymentDueDate.Should().Be(new DateTime(2026, 7, 1), "BT-9 survit au Rebuild");
        enriched.IsB2cReportingDeclaration.Should().BeTrue("le marqueur 10.3 survit au Rebuild");
        enriched.SellerFees.Should().ContainSingle().Which.LotReference.Should().Be("no_ba=42");
        enriched.BuyerFees.Should().ContainSingle().Which.NetAmount.Should().Be(30.00m);
        enriched.InvoicePeriod.Should().NotBeNull("BG-14 survit au Rebuild (régression du merge)");
        enriched.InvoicePeriod!.StartDate.Should().Be(new DateTime(2026, 6, 1));
        enriched.InvoicePeriod.EndDate.Should().Be(new DateTime(2026, 6, 30));
    }

    private static TenantProfileDto ProfileWithCountry(string siren, string raisonSociale, string country) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = Guid.NewGuid(),
        Siren = siren,
        RaisonSociale = raisonSociale,
        Street = "1 Hauptstrasse",
        PostalCode = "10115",
        City = "Berlin",
        Country = country,
        Statut = "Actif",
        CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
    };

    private static PivotDocumentDto Pivot(PivotPartyDto? supplier, OperationCategory? operationCategory) =>
        new(
            sourceDocumentKind: "FAC",
            number: "A-2026-0001",
            issueDate: new DateTime(2026, 6, 1),
            sourceReference: "demoerpa:A-2026-0001",
            supplier: supplier,
            totals: new PivotTotalsDto(100m, 20m, 120m),
            operationCategory: operationCategory);

    private static TenantProfileDto Profile(string siren, string raisonSociale) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = Guid.NewGuid(),
        Siren = siren,
        RaisonSociale = raisonSociale,
        Street = "1 quai du Port",
        PostalCode = "56100",
        City = "Lorient",
        Country = "FR",
        Statut = "Actif",
        CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
    };

    private static FiscalSettingsDto Fiscal(string operationCategory) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = Guid.NewGuid(),
        OperationCategory = operationCategory,
        CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
    };
}
