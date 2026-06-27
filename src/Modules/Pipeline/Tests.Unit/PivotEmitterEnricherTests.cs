namespace Liakont.Modules.Pipeline.Tests.Unit;

using System;
using System.Linq;
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

    [Fact]
    public void Injects_payment_terms_and_three_legal_notes_from_tenant_mentions_when_document_carries_none()
    {
        // BUG-26 (F12-A §3.4) : le document ne porte ni termes de paiement ni notes → l'enrich injecte le défaut
        // TENANT : BT-20 (termes de paiement) + 3 notes légales FR mappées au bon code sujet (PMD/PMT/AAB). Le
        // CONTENU vient du tenant, seul le mapping mention → code est figé (CLAUDE.md n°2).
        PivotDocumentDto enriched = PivotEmitterEnricher.Enrich(
            Pivot(supplier: null, operationCategory: null),
            Profile("123456782", "SEM Keroman"),
            Fiscal("LivraisonBiens"),
            Mentions(
                paymentTerms: "Paiement à 30 jours.",
                latePenalty: "Pénalités de retard au taux légal.",
                recoveryFee: "Indemnité forfaitaire de 40 €.",
                discount: "Pas d'escompte pour paiement anticipé."));

        enriched.PaymentTerms.Should().Be("Paiement à 30 jours.");
        enriched.Notes.Should().NotBeNull();
        enriched.Notes!.Should().HaveCount(3);

        NoteFor(enriched, PivotEmitterEnricher.LatePenaltySubjectCode).Should().Be("Pénalités de retard au taux légal.");
        NoteFor(enriched, PivotEmitterEnricher.RecoveryFeeSubjectCode).Should().Be("Indemnité forfaitaire de 40 €.");
        NoteFor(enriched, PivotEmitterEnricher.DiscountSubjectCode).Should().Be("Pas d'escompte pour paiement anticipé.");
    }

    [Fact]
    public void Does_not_overwrite_payment_terms_and_notes_already_carried_by_the_document()
    {
        // Surcharge par document (la source PRIME, F12-A §3.4) : un document portant DÉJÀ ses termes de paiement
        // et ses notes n'est pas écrasé par le défaut tenant — exactement comme l'émetteur 389.
        var documentNotes = new[] { new PivotDocumentNoteDto("Note portée par la source.", "PMD") };
        var document = new PivotDocumentDto(
            sourceDocumentKind: "FAC",
            number: "A-2026-0003",
            issueDate: new DateTime(2026, 6, 1),
            sourceReference: "demoerpa:A-2026-0003",
            supplier: null,
            totals: new PivotTotalsDto(100m, 20m, 120m),
            operationCategory: null,
            paymentTerms: "Termes portés par la source.",
            notes: documentNotes);

        PivotDocumentDto enriched = PivotEmitterEnricher.Enrich(
            document,
            Profile("123456782", "SEM Keroman"),
            Fiscal("LivraisonBiens"),
            Mentions(
                paymentTerms: "Défaut tenant ignoré.",
                latePenalty: "PMD tenant ignoré.",
                recoveryFee: "PMT tenant ignoré.",
                discount: "AAB tenant ignoré."));

        enriched.PaymentTerms.Should().Be("Termes portés par la source.", "la valeur du document prime sur le défaut tenant");
        enriched.Notes.Should().BeSameAs(documentNotes, "les notes portées par le document ne sont pas écrasées par le défaut tenant");
    }

    [Fact]
    public void Leaves_payment_terms_and_notes_untouched_when_no_tenant_mentions_are_provided()
    {
        // Mentions null (additif) : aucune injection — ni termes de paiement, ni notes. Le document inchangé est
        // renvoyé tel quel (un document FR B2B sans mentions sera bloqué au CHECK, jamais inventé — CLAUDE.md n°2).
        PivotDocumentDto enriched = PivotEmitterEnricher.Enrich(
            Pivot(supplier: null, operationCategory: null),
            Profile("123456782", "SEM Keroman"),
            Fiscal("LivraisonBiens"),
            mentions: null);

        enriched.PaymentTerms.Should().BeNull();
        enriched.Notes.Should().BeNull();
    }

    [Fact]
    public void Omits_a_note_whose_tenant_mention_is_blank_keeping_only_the_populated_ones()
    {
        // Une mention vide → note OMISE (rien à émettre) : seules les mentions renseignées produisent une note.
        PivotDocumentDto enriched = PivotEmitterEnricher.Enrich(
            Pivot(supplier: null, operationCategory: null),
            Profile("123456782", "SEM Keroman"),
            Fiscal("LivraisonBiens"),
            Mentions(paymentTerms: null, latePenalty: "Pénalités de retard.", recoveryFee: null, discount: "   "));

        enriched.PaymentTerms.Should().BeNull("une mention de termes de paiement vide n'est pas injectée");
        enriched.Notes.Should().NotBeNull();
        enriched.Notes!.Should().ContainSingle("seule la mention renseignée (PMD) produit une note");
        enriched.Notes![0].SubjectCode.Should().Be(PivotEmitterEnricher.LatePenaltySubjectCode);
    }

    private static string? NoteFor(PivotDocumentDto enriched, string subjectCode) =>
        enriched.Notes?.SingleOrDefault(note => note.SubjectCode == subjectCode)?.Content;

    private static BillingMentionsDto Mentions(
        string? paymentTerms,
        string? latePenalty,
        string? recoveryFee,
        string? discount) => new()
        {
            Id = Guid.NewGuid(),
            CompanyId = Guid.NewGuid(),
            PaymentTerms = paymentTerms,
            LatePenaltyTerms = latePenalty,
            RecoveryFeeTerms = recoveryFee,
            DiscountTerms = discount,
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };

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
