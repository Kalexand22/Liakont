namespace Liakont.Host.Tests.Unit.Components;

using System;
using System.Collections.Generic;
using System.Linq;
using Bunit;
using FluentAssertions;
using Liakont.Host.Components;
using Liakont.Host.Ged;
using Liakont.Modules.Ged.Contracts.Queries;
using Xunit;

// Rendu PUR de la fiche document GED (/ged/document/{id}, GED09b, F19 §6.7). La vue reçoit un modèle assemblé
// (masquage confidentiel DÉJÀ appliqué server-side : les axes/entités confidentiels sont absents du modèle) et
// n'ajoute aucune logique métier. On couvre : en-tête + statut, verdicts d'intégrité, aperçu (présent/absent),
// mise en forme des axes (decimal fr-FR, booléen), états vides et rattachement fiscal.
public sealed class GedDocumentViewTests : BunitContext
{
    public GedDocumentViewTests()
    {
        // StatusBadge / SectionCard peuvent appeler du JS : mode permissif.
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Should_Render_Header_Title_Status_And_Retention()
    {
        var model = BuildModel(title: "Bordereau acheteur 42", status: "indexed", retention: "tenant_bounded");

        var cut = Render<GedDocumentView>(p => p.Add(v => v.Model, model));

        cut.Find("[data-testid='ged-document-title']").TextContent.Should().Contain("Bordereau acheteur 42");
        cut.Find("[data-testid='ged-document-status']").TextContent.Should().Contain("Indexé");
        cut.Find("[data-testid='ged-document-retention']").TextContent.Should().Contain("Borné au tenant");
    }

    [Fact]
    public void Should_Show_A_Warning_And_Reason_When_Deferred()
    {
        var model = BuildModel(status: "deferred", deferReason: "Type de document « PV » sans profil de mapping.");

        var cut = Render<GedDocumentView>(p => p.Add(v => v.Model, model));

        cut.Find("[data-testid='ged-document-status']").TextContent.Should().Contain("En attente d'indexation");
        cut.Find("[data-testid='ged-document-defer']").TextContent.Should().Contain("PV");
    }

    [Fact]
    public void Should_Show_Verified_Integrity_With_Indexed_Hash()
    {
        var model = BuildModel(integrity: new GedDocumentIntegrityView(
            GedDocumentIntegrityState.Verified, "abc123", "abc123", null));

        var cut = Render<GedDocumentView>(p => p.Add(v => v.Model, model));

        cut.Find("[data-testid='ged-document-integrity-state']").TextContent.Should().Contain("Intégrité vérifiée");
        cut.Find("[data-testid='ged-document-integrity-hash']").TextContent.Should().Contain("abc123");
    }

    [Fact]
    public void Should_Show_Altered_Integrity_With_Detail_And_Recomputed_Hash()
    {
        var model = BuildModel(integrity: new GedDocumentIntegrityView(
            GedDocumentIntegrityState.Altered, "abc123", "def456", "Le contenu du paquet a été modifié depuis son scellement."));

        var cut = Render<GedDocumentView>(p => p.Add(v => v.Model, model));

        cut.Find("[data-testid='ged-document-integrity-state']").TextContent.Should().Contain("Intégrité compromise");
        cut.Find("[data-testid='ged-document-integrity-recomputed']").TextContent.Should().Contain("def456");
        cut.Find("[data-testid='ged-document-integrity-detail']").TextContent.Should().Contain("modifié");
    }

    [Fact]
    public void Should_Show_NotArchived_Integrity()
    {
        var model = BuildModel(integrity: new GedDocumentIntegrityView(
            GedDocumentIntegrityState.NotArchived, null, null, null));

        var cut = Render<GedDocumentView>(p => p.Add(v => v.Model, model));

        cut.Find("[data-testid='ged-document-integrity-state']").TextContent.Should().Contain("Non archivé");
    }

    [Fact]
    public void Should_Show_Fiscal_Link_And_Fiscal_Integrity_Note_When_Fiscal_Linked()
    {
        var fiscalId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");
        var model = BuildModel(
            isFiscalLinked: true,
            fiscalDocumentId: fiscalId,
            integrity: new GedDocumentIntegrityView(GedDocumentIntegrityState.FiscalLinked, "abc123", null, null));

        var cut = Render<GedDocumentView>(p => p.Add(v => v.Model, model));

        cut.Find("[data-testid='ged-document-integrity-state']").TextContent.Should().Contain("Document fiscal lié");
        cut.Find("[data-testid='ged-document-integrity-fiscal']").TextContent.Should().Contain("chaîne de hashes");
        cut.Find("[data-testid='ged-document-fiscal-anchor']").GetAttribute("href").Should().Be($"/documents/{fiscalId}");
    }

    [Fact]
    public void Should_Render_A_Sandboxed_Preview_When_Readable_Html_Is_Present()
    {
        var model = BuildModel(previewHtml: "<p>Bordereau lisible</p>");

        var cut = Render<GedDocumentView>(p => p.Add(v => v.Model, model));

        var preview = cut.Find("[data-testid='ged-document-preview']");
        preview.GetAttribute("sandbox").Should().Be(string.Empty, "l'aperçu est rendu en bac à sable (sandbox vide = restrictions maximales)");
        preview.GetAttribute("srcdoc").Should().Contain("Bordereau lisible");
    }

    [Fact]
    public void Should_Show_A_Placeholder_When_No_Preview_Is_Available()
    {
        var model = BuildModel(previewHtml: null);

        var cut = Render<GedDocumentView>(p => p.Add(v => v.Model, model));

        cut.FindAll("[data-testid='ged-document-preview']").Should().BeEmpty();
        cut.Find("[data-testid='ged-document-preview-none']").TextContent.Should().Contain("Aucun aperçu");
    }

    [Fact]
    public void Should_Format_A_Number_Axis_As_French_Decimal_With_Unit()
    {
        var axes = new List<GedManagedAxisValue>
        {
            new()
            {
                Code = "montant_ht_cumule", Label = "Montant HT cumulé", DataType = "number",
                Unit = "EUR", ValueScale = 2, ValueNumber = 1234.5m,
            },
        };
        var model = BuildModel(axes: axes);

        var cut = Render<GedDocumentView>(p => p.Add(v => v.Model, model));

        var axis = cut.Find("[data-testid='ged-document-axis']");
        axis.TextContent.Should().Contain("Montant HT cumulé");

        // Assertion sur la partie décimale seule : le séparateur de milliers fr-FR est un espace INSÉCABLE.
        axis.TextContent.Should().Contain("234,50").And.Contain("EUR");
    }

    [Fact]
    public void Should_Render_String_And_Boolean_Axes_And_Linked_Entities()
    {
        var axes = new List<GedManagedAxisValue>
        {
            new() { Code = "numero_lot", Label = "Numéro de lot", DataType = "string", ValueString = "LOT-42" },
            new() { Code = "solde", Label = "Soldé", DataType = "boolean", ValueBoolean = true },
        };
        var entities = new List<GedManagedEntityLink>
        {
            new("acheteur", "acheteur", "Acheteur", "MARTIN SARL", "12345678900011"),
        };
        var model = BuildModel(axes: axes, entities: entities);

        var cut = Render<GedDocumentView>(p => p.Add(v => v.Model, model));

        var renderedAxes = cut.FindAll("[data-testid='ged-document-axis']");
        renderedAxes.Should().HaveCount(2);
        cut.Find("[data-testid='ged-document-axes']").TextContent.Should().Contain("LOT-42").And.Contain("Oui");

        var entity = cut.Find("[data-testid='ged-document-entity']");
        entity.TextContent.Should().Contain("acheteur").And.Contain("MARTIN SARL").And.Contain("12345678900011");
    }

    [Fact]
    public void Should_Show_Empty_Placeholders_When_No_Axes_Or_Entities()
    {
        var model = BuildModel();

        var cut = Render<GedDocumentView>(p => p.Add(v => v.Model, model));

        cut.Find("[data-testid='ged-document-axes-empty']").TextContent.Should().Contain("Aucun axe");
        cut.Find("[data-testid='ged-document-entities-empty']").TextContent.Should().Contain("Aucune entité");
    }

    private static GedDocumentDetailViewModel BuildModel(
        string title = "Document GED",
        string? docKind = null,
        string status = "indexed",
        string retention = "tenant_bounded",
        string? deferReason = null,
        GedDocumentIntegrityView? integrity = null,
        string? previewHtml = null,
        bool isFiscalLinked = false,
        Guid? fiscalDocumentId = null,
        IReadOnlyList<GedManagedAxisValue>? axes = null,
        IReadOnlyList<GedManagedEntityLink>? entities = null) => new()
    {
        Id = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001"),
        Title = title,
        DocKind = docKind,
        Status = status,
        RetentionClass = retention,
        DeferReason = deferReason,
        Integrity = integrity ?? new GedDocumentIntegrityView(GedDocumentIntegrityState.NotArchived, null, null, null),
        PreviewHtml = previewHtml,
        IsFiscalLinked = isFiscalLinked,
        FiscalDocumentId = fiscalDocumentId,
        CreatedUtc = new DateTimeOffset(2026, 6, 1, 10, 30, 0, TimeSpan.Zero),
        UpdatedUtc = null,
        Axes = axes ?? [],
        Entities = entities ?? [],
    };
}
