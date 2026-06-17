namespace Liakont.Modules.Ingestion.Tests.Unit;

using System;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Ingestion.Infrastructure;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Xunit;

/// <summary>
/// Remplissage de l'émetteur à l'ingestion depuis le profil tenant (ADR-0023 amendé) : l'agent ne porte
/// plus l'identité émetteur, la plateforme la remplit ICI avant la sérialisation/staging. Garde les
/// invariants fiscaux : remplissage QUAND ABSENT (un émetteur déjà porté — ex. 389 — n'est pas écrasé),
/// profil incomplet → champ laissé nul (bloqué au CHECK, jamais deviné — CLAUDE.md n°2/n°3), nature
/// d'opération parsée PAR NOM (les deux enums OperationCategory ont des valeurs numériques différentes).
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
