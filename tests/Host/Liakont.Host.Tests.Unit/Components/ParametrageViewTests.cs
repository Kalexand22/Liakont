namespace Liakont.Host.Tests.Unit.Components;

using System;
using System.Collections.Generic;
using Bunit;
using FluentAssertions;
using Liakont.Host.Components;
using Liakont.Host.Parametrage;
using Liakont.Modules.Archive.Contracts;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class ParametrageViewTests : BunitContext
{
    public ParametrageViewTests()
    {
        // RadzenButton (StratumButton) appelle du JS sur certaines interactions : mode permissif.
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddBrowserTimeZoneStub();
    }

    [Fact]
    public void Should_Render_Profile_Fields_When_Present()
    {
        var model = BuildModel(profile: BuildProfile());

        var cut = Render<ParametrageView>(p => p.Add(v => v.Model, model));

        cut.Find("[data-testid='parametrage-profil-siren']").TextContent.Should().Contain("123456782");
        cut.Find("[data-testid='parametrage-profil-raison']").TextContent.Should().Contain("Étude des Enchères");
        cut.Find("[data-testid='parametrage-profil-contact']").TextContent.Should().Contain("alerte@exemple.fr");
    }

    [Fact]
    public void Should_Show_Profile_Absent_When_Null()
    {
        var cut = Render<ParametrageView>(p => p.Add(v => v.Model, BuildModel(profile: null)));

        cut.FindAll("[data-testid='parametrage-profil-absent']").Should().ContainSingle();
        cut.FindAll("[data-testid='parametrage-profil-content']").Should().BeEmpty();
    }

    [Fact]
    public void Should_Show_Incomplete_Banner_When_Profile_Null()
    {
        var cut = Render<ParametrageView>(p => p.Add(v => v.Model, BuildModel(profile: null)));

        var banner = cut.Find("[data-testid='parametrage-profile-incomplete']");
        banner.TextContent.Should().Contain("PARAMÉTRAGE INCOMPLET");
        banner.TextContent.Should().Contain("suspendu");
    }

    [Fact]
    public void Should_Not_Show_Incomplete_Banner_When_Profile_Present()
    {
        var cut = Render<ParametrageView>(p => p.Add(v => v.Model, BuildModel(profile: BuildProfile())));

        cut.FindAll("[data-testid='parametrage-profile-incomplete']").Should().BeEmpty();
    }

    [Fact]
    public void Should_Show_Fiscal_Values_When_Present()
    {
        var fiscal = BuildFiscal(vatOnDebits: true, operationCategory: "PRESTATION_SERVICE", reportingFrequency: "MENSUELLE");

        var cut = Render<ParametrageView>(p => p.Add(v => v.Model, BuildModel(fiscal: fiscal)));

        cut.Find("[data-testid='parametrage-fiscal-vatondebits']").TextContent.Should().Contain("Oui");
        cut.Find("[data-testid='parametrage-fiscal-operationcategory']").TextContent.Should().Contain("PRESTATION_SERVICE");
        cut.Find("[data-testid='parametrage-fiscal-reportingfrequency']").TextContent.Should().Contain("MENSUELLE");
        cut.FindAll("[data-testid='parametrage-fiscal-operationcategory-pending']").Should().BeEmpty();
    }

    [Fact]
    public void Should_Show_Pending_Alert_When_A_Fiscal_Param_Is_Null()
    {
        // Catégorie d'opération absente : alerte « décision en attente », jamais de valeur devinée.
        var fiscal = BuildFiscal(vatOnDebits: false, operationCategory: null, reportingFrequency: "TRIMESTRIELLE");

        var cut = Render<ParametrageView>(p => p.Add(v => v.Model, BuildModel(fiscal: fiscal)));

        cut.FindAll("[data-testid='parametrage-fiscal-operationcategory-pending']").Should().ContainSingle();
        cut.FindAll("[data-testid='parametrage-fiscal-operationcategory']").Should().BeEmpty();
        cut.Find("[data-testid='parametrage-fiscal-vatondebits']").TextContent.Should().Contain("Non");
        cut.Find("[data-testid='parametrage-fiscal-reportingfrequency']").TextContent.Should().Contain("TRIMESTRIELLE");
    }

    [Fact]
    public void Should_Show_All_Fiscal_Params_Pending_When_FiscalSettings_Null()
    {
        var cut = Render<ParametrageView>(p => p.Add(v => v.Model, BuildModel(fiscal: null)));

        cut.FindAll("[data-testid='parametrage-fiscal-vatondebits-pending']").Should().ContainSingle();
        cut.FindAll("[data-testid='parametrage-fiscal-operationcategory-pending']").Should().ContainSingle();
        cut.FindAll("[data-testid='parametrage-fiscal-reportingfrequency-pending']").Should().ContainSingle();
    }

    [Fact]
    public void Should_Mask_Pa_Secret_And_Show_Account_Metadata()
    {
        var account = BuildPaAccount(hasApiKey: true);
        var model = BuildModel(paAccounts: [new PaAccountSettingsDto { Account = account, PluginAvailable = true, Capabilities = BuildCapabilities() }]);

        var cut = Render<ParametrageView>(p => p.Add(v => v.Model, model));

        var secret = cut.Find("[data-testid='parametrage-pa-secret']");
        secret.TextContent.Should().Contain("masquée");

        // Aucune clé en clair : seul l'état « configurée (masquée) » est rendu.
        secret.TextContent.Should().NotContain("SECRET-KEY");
    }

    [Fact]
    public void Should_Not_Render_Capability_Details_On_The_Hub()
    {
        // Le DÉTAIL des capacités a déménagé sur l'écran Comptes PA (lot polish UX/UI) : le hub
        // reste une synthèse — plus aucune liste de capacités ici, même avec un plug-in chargé.
        var caps = BuildCapabilities(b2c: true, domesticPayment: false);
        var model = BuildModel(paAccounts: [new PaAccountSettingsDto { Account = BuildPaAccount(), PluginAvailable = true, Capabilities = caps }]);

        var cut = Render<ParametrageView>(p => p.Add(v => v.Model, model));

        cut.FindAll("[data-testid='parametrage-pa-capabilities']").Should().BeEmpty();
        cut.FindAll("[data-testid='parametrage-pa-capability']").Should().BeEmpty();
    }

    [Fact]
    public void Should_Show_Plugin_Unavailable_When_No_Capabilities()
    {
        // Seul le REPLI reste signalé sur le hub : un plug-in absent rend le compte inutilisable.
        var model = BuildModel(paAccounts: [new PaAccountSettingsDto { Account = BuildPaAccount(), PluginAvailable = false, Capabilities = null }]);

        var cut = Render<ParametrageView>(p => p.Add(v => v.Model, model));

        cut.FindAll("[data-testid='parametrage-pa-plugin-unavailable']").Should().ContainSingle();
        cut.FindAll("[data-testid='parametrage-pa-capabilities']").Should().BeEmpty();
    }

    [Fact]
    public void Should_Link_To_The_Agents_Management_Page()
    {
        // La carte Agents offre le même geste de navigation que les autres cartes du hub (elle
        // n'avait qu'un texte sans lien — lot polish UX/UI).
        var cut = Render<ParametrageView>(p => p.Add(v => v.Model, BuildModel()));

        cut.FindAll("[data-testid='parametrage-agents-link']").Should().ContainSingle();

        // StratumButton(Href) navigue au clic : on vérifie la CIBLE réelle.
        cut.Find("[data-testid='parametrage-agents-link']").Click();
        cut.Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith("/agents");
    }

    [Fact]
    public void Should_Show_Pa_None_When_No_Account()
    {
        var cut = Render<ParametrageView>(p => p.Add(v => v.Model, BuildModel()));

        cut.FindAll("[data-testid='parametrage-pa-none']").Should().ContainSingle();
    }

    [Fact]
    public void Should_Link_To_The_Pa_Accounts_Management_Page()
    {
        // Le lien « Gérer les comptes PA » est offert même sans compte (création du premier depuis l'écran dédié)
        // — la page cible porte la garde liakont.settings (FIX01c).
        var cut = Render<ParametrageView>(p => p.Add(v => v.Model, BuildModel()));

        cut.FindAll("[data-testid='parametrage-pa-link']").Should().ContainSingle();
    }

    [Fact]
    public void Should_Link_To_The_Alert_Settings_Page()
    {
        // Le lien « Paramétrer les alertes » mène à la page dédiée (garde liakont.settings, FIX210).
        var cut = Render<ParametrageView>(p => p.Add(v => v.Model, BuildModel()));

        cut.FindAll("[data-testid='parametrage-alertes-link']").Should().ContainSingle();

        // StratumButton(Href) navigue au clic : on vérifie la CIBLE réelle.
        cut.Find("[data-testid='parametrage-alertes-link']").Click();
        cut.Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith("/parametrage/alertes");
    }

    [Fact]
    public void Should_Link_To_The_Fiscal_Settings_Page()
    {
        // Le bouton « Modifier les paramètres fiscaux » mène à la page dédiée (FIX301).
        var cut = Render<ParametrageView>(p => p.Add(v => v.Model, BuildModel()));

        cut.FindAll("[data-testid='parametrage-fiscal-link']").Should().ContainSingle();

        // StratumButton(Href) navigue au clic : on vérifie la CIBLE réelle.
        cut.Find("[data-testid='parametrage-fiscal-link']").Click();
        cut.Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith("/parametrage/fiscal");
    }

    [Fact]
    public void Should_Not_Render_A_Separate_Billing_Mentions_Card()
    {
        // BUG-26 (ajustement PO) : les mentions de facturation vivent DANS la page « Paramètres fiscaux »,
        // plus comme carte séparée du hub. La vue d'ensemble revient à 9 cartes.
        var cut = Render<ParametrageView>(p => p.Add(v => v.Model, BuildModel()));

        cut.FindAll("[data-testid='parametrage-mentions']").Should().BeEmpty();
        cut.FindAll("[data-testid='parametrage-mentions-link']").Should().BeEmpty();
    }

    [Fact]
    public void Should_Render_Tva_Validated_With_Validator_And_Link()
    {
        var tva = new TvaMappingSummaryDto
        {
            MappingVersion = "v3",
            IsValidated = true,
            ValidatedBy = "M. Comptable",
            ValidatedDate = new DateOnly(2026, 5, 20),
            DefaultBehavior = "BLOCK",
            RuleCount = 4,
        };

        var cut = Render<ParametrageView>(p => p.Add(v => v.Model, BuildModel(tva: tva)));

        var validated = cut.Find("[data-testid='parametrage-tva-validated']");
        validated.TextContent.Should().Contain("Validée par M. Comptable");
        validated.TextContent.Should().Contain("20/05/2026");
        cut.FindAll("[data-testid='parametrage-tva-link']").Should().ContainSingle();
    }

    [Fact]
    public void Should_Render_Tva_Not_Validated_Banner()
    {
        var tva = new TvaMappingSummaryDto
        {
            MappingVersion = "v1",
            IsValidated = false,
            DefaultBehavior = "BLOCK",
            RuleCount = 2,
        };

        var cut = Render<ParametrageView>(p => p.Add(v => v.Model, BuildModel(tva: tva)));

        var banner = cut.Find("[data-testid='parametrage-tva-not-validated']");
        banner.TextContent.Should().Contain("NON VALIDÉE");
        banner.TextContent.Should().Contain("Les envois sont suspendus");
    }

    [Fact]
    public void Should_Render_Tva_None_When_Null()
    {
        var cut = Render<ParametrageView>(p => p.Add(v => v.Model, BuildModel(tva: null)));

        cut.FindAll("[data-testid='parametrage-tva-none']").Should().ContainSingle();
        cut.FindAll("[data-testid='parametrage-tva-link']").Should().ContainSingle();
    }

    [Fact]
    public void Should_List_Agents_With_State_Heartbeat_Version()
    {
        var agents = new List<AgentStatusLine>
        {
            new("Agent A", new DateTimeOffset(2026, 6, 8, 10, 0, 0, TimeSpan.Zero), "1.2.3", false),
            new("Agent B", null, null, true),
        };

        var cut = Render<ParametrageView>(p => p.Add(v => v.Model, BuildModel(agents: agents)));

        var lines = cut.FindAll("[data-testid='parametrage-agent-line']");
        lines.Should().HaveCount(2);
        lines[0].TextContent.Should().Contain("Agent A");
        lines[0].TextContent.Should().Contain("Actif");
        lines[0].TextContent.Should().Contain("1.2.3");
        lines[1].TextContent.Should().Contain("Révoqué");

        // Sans aucun contact, la méta « Dernier contact : jamais » n'est plus affichée (lot 2 : le
        // badge porte l'information — « Révoqué » prime sur « Jamais connecté »).
        lines[1].TextContent.Should().NotContain("Dernier contact");
        lines[1].TextContent.Should().Contain("inconnue");
    }

    [Fact]
    public void Should_Show_Agents_None_When_Empty()
    {
        var cut = Render<ParametrageView>(p => p.Add(v => v.Model, BuildModel()));

        cut.FindAll("[data-testid='parametrage-agents-none']").Should().ContainSingle();
    }

    [Fact]
    public void Should_Not_Show_Integrity_Report_Initially_But_Show_Button()
    {
        var cut = Render<ParametrageView>(p => p.Add(v => v.Model, BuildModel()));

        cut.FindAll("[data-testid='parametrage-integrite-btn']").Should().ContainSingle();
        cut.FindAll("[data-testid='parametrage-integrite-report']").Should().BeEmpty();
        cut.FindAll("[data-testid='parametrage-integrite-running']").Should().BeEmpty();
    }

    [Fact]
    public void Should_Show_Integrity_Report_When_Fully_Verified()
    {
        var report = BuildReport(isIntact: true, isChainAnchored: true, isFullyVerified: true, summary: "Coffre vérifié : 12 entrées intègres.");

        var cut = Render<ParametrageView>(p => p
            .Add(v => v.Model, BuildModel())
            .Add(v => v.IntegrityReport, report));

        var block = cut.Find("[data-testid='parametrage-integrite-report']");
        block.TextContent.Should().Contain("Coffre intègre");
        cut.Find("[data-testid='parametrage-integrite-summary']").TextContent.Should().Contain("12 entrées intègres");
        cut.FindAll("[data-testid='parametrage-integrite-break']").Should().BeEmpty();
    }

    [Fact]
    public void Should_Show_Anomaly_And_Break_When_Not_Intact()
    {
        var report = BuildReport(isIntact: false, isChainAnchored: false, isFullyVerified: false, summary: "Rupture détectée.", firstBreakDetail: "Entrée 7 : contenu altéré.");

        var cut = Render<ParametrageView>(p => p
            .Add(v => v.Model, BuildModel())
            .Add(v => v.IntegrityReport, report));

        var block = cut.Find("[data-testid='parametrage-integrite-report']");
        block.TextContent.Should().Contain("Anomalie détectée");
        cut.Find("[data-testid='parametrage-integrite-break']").TextContent.Should().Contain("Entrée 7 : contenu altéré.");
    }

    [Fact]
    public void Should_Show_Verifying_State_And_Disable_Button()
    {
        var cut = Render<ParametrageView>(p => p
            .Add(v => v.Model, BuildModel())
            .Add(v => v.IsVerifying, true));

        cut.FindAll("[data-testid='parametrage-integrite-running']").Should().ContainSingle();
        cut.Find("[data-testid='parametrage-integrite-btn']").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void Should_Show_Integrity_Error_When_Set()
    {
        var cut = Render<ParametrageView>(p => p
            .Add(v => v.Model, BuildModel())
            .Add(v => v.IntegrityError, "La vérification d'intégrité a échoué. Réessayez plus tard."));

        cut.Find("[data-testid='parametrage-integrite-error']").TextContent.Should().Contain("a échoué");
        cut.FindAll("[data-testid='parametrage-integrite-report']").Should().BeEmpty();
    }

    [Fact]
    public void Should_Invoke_Callback_When_Verify_Button_Clicked()
    {
        var clicked = false;

        var cut = Render<ParametrageView>(p => p
            .Add(v => v.Model, BuildModel())
            .Add(v => v.OnVerifyIntegrity, EventCallback.Factory.Create(this, () => clicked = true)));

        cut.Find("[data-testid='parametrage-integrite-btn']").Click();

        clicked.Should().BeTrue();
    }

    [Fact]
    public void Should_Hide_Audit_Export_Without_Read_Permission()
    {
        // Sans liakont.read, aucune section d'export d'audit (défaut : CanExportAudit = false).
        var cut = Render<ParametrageView>(p => p.Add(v => v.Model, BuildModel()));

        cut.FindAll("[data-testid='parametrage-audit-export']").Should().BeEmpty();
        cut.FindAll("[data-testid='parametrage-exports-audit']").Should().BeEmpty();
        cut.FindAll("[data-testid='parametrage-exports-tenant']").Should().BeEmpty();
    }

    [Fact]
    public void Should_Require_At_Least_One_Bound_For_Audit_Export()
    {
        var cut = Render<ParametrageView>(p => p
            .Add(v => v.Model, BuildModel())
            .Add(v => v.CanExportAudit, true));

        // Sans borne saisie : pas de lien de téléchargement, message de validation explicite.
        cut.FindAll("[data-testid='parametrage-audit-export-link']").Should().BeEmpty();
        cut.Find("[data-testid='parametrage-audit-export-invalid']").TextContent.Should().Contain("au moins une borne");
    }

    [Fact]
    public void Should_Offer_Audit_Export_Link_With_Single_Bound()
    {
        var cut = Render<ParametrageView>(p => p
            .Add(v => v.Model, BuildModel())
            .Add(v => v.CanExportAudit, true));

        // Une seule borne « Du » suffit : le lien apparaît, pointe sur l'endpoint API03, en téléchargement.
        cut.Find("[data-testid='parametrage-audit-from']").Change("2026-05-01");

        var link = cut.Find("[data-testid='parametrage-audit-export-link']");
        link.GetAttribute("href").Should().Be("/api/v1/audit-export?from=2026-05-01");
        link.HasAttribute("download").Should().BeTrue();
    }

    [Fact]
    public void Should_Build_Audit_Url_With_Both_Bounds()
    {
        var cut = Render<ParametrageView>(p => p
            .Add(v => v.Model, BuildModel())
            .Add(v => v.CanExportAudit, true));

        cut.Find("[data-testid='parametrage-audit-from']").Change("2026-05-01");
        cut.Find("[data-testid='parametrage-audit-to']").Change("2026-05-31");

        cut.Find("[data-testid='parametrage-audit-export-link']").GetAttribute("href")
            .Should().Be("/api/v1/audit-export?from=2026-05-01&to=2026-05-31");
    }

    [Fact]
    public void Should_Reject_Inverted_Audit_Bounds()
    {
        var cut = Render<ParametrageView>(p => p
            .Add(v => v.Model, BuildModel())
            .Add(v => v.CanExportAudit, true));

        cut.Find("[data-testid='parametrage-audit-from']").Change("2026-05-31");
        cut.Find("[data-testid='parametrage-audit-to']").Change("2026-05-01");

        cut.FindAll("[data-testid='parametrage-audit-export-link']").Should().BeEmpty();
        cut.Find("[data-testid='parametrage-audit-export-invalid']").TextContent.Should().Contain("précède");
    }

    [Fact]
    public void Should_Hide_Tenant_Export_Without_Settings_Permission()
    {
        // Avec liakont.read seul, l'export d'audit est offert mais PAS la réversibilité (liakont.settings).
        var cut = Render<ParametrageView>(p => p
            .Add(v => v.Model, BuildModel())
            .Add(v => v.CanExportAudit, true));

        cut.FindAll("[data-testid='parametrage-tenant-export']").Should().BeEmpty();
    }

    [Fact]
    public void Should_Confirm_Before_Offering_Tenant_Reversibility_Download()
    {
        var cut = Render<ParametrageView>(p => p
            .Add(v => v.Model, BuildModel())
            .Add(v => v.CanExportTenant, true));

        // Bouton présent, dialog de confirmation absente initialement.
        cut.FindAll("[data-testid='parametrage-tenant-export-btn']").Should().ContainSingle();
        cut.FindAll("[data-testid='parametrage-tenant-export-confirm-dialog']").Should().BeEmpty();

        cut.Find("[data-testid='parametrage-tenant-export-btn']").Click();

        // Après confirmation explicite : lien de téléchargement vers l'endpoint de réversibilité (API03).
        var confirm = cut.Find("[data-testid='parametrage-tenant-export-confirm']");
        confirm.GetAttribute("href").Should().Be("/api/v1/tenant-export");
        confirm.HasAttribute("download").Should().BeTrue();
    }

    [Fact]
    public void Should_Cancel_Tenant_Reversibility_Confirmation()
    {
        var cut = Render<ParametrageView>(p => p
            .Add(v => v.Model, BuildModel())
            .Add(v => v.CanExportTenant, true));

        cut.Find("[data-testid='parametrage-tenant-export-btn']").Click();
        cut.Find("[data-testid='parametrage-tenant-export-cancel']").Click();

        cut.FindAll("[data-testid='parametrage-tenant-export-confirm-dialog']").Should().BeEmpty();
        cut.FindAll("[data-testid='parametrage-tenant-export-btn']").Should().ContainSingle();
    }

    private static ParametrageViewModel BuildModel(
        TenantProfileDto? profile = null,
        FiscalSettingsDto? fiscal = null,
        TvaMappingSummaryDto? tva = null,
        IReadOnlyList<PaAccountSettingsDto>? paAccounts = null,
        IReadOnlyList<AgentStatusLine>? agents = null) => new()
        {
            Profile = profile,
            FiscalSettings = fiscal,
            TvaMapping = tva,
            PaAccounts = paAccounts ?? [],
            Agents = agents ?? [],
        };

    private static TenantProfileDto BuildProfile() => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = Guid.NewGuid(),
        Siren = "123456782",
        RaisonSociale = "Étude des Enchères",
        Street = "1 rue de l'Exemple",
        PostalCode = "35000",
        City = "Rennes",
        Country = "FR",
        ContactEmailAlerte = "alerte@exemple.fr",
        Statut = "Actif",
        CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
    };

    private static FiscalSettingsDto BuildFiscal(bool? vatOnDebits, string? operationCategory, string? reportingFrequency) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = Guid.NewGuid(),
        VatOnDebits = vatOnDebits,
        OperationCategory = operationCategory,
        ReportingFrequency = reportingFrequency,
        CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
    };

    private static PaAccountDto BuildPaAccount(bool hasApiKey = true) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = Guid.NewGuid(),
        PluginType = "B2Brouter",
        Environment = "Production",
        AccountIdentifiers = "compte-exemple",
        HasApiKey = hasApiKey,
        IsActive = true,
        CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
    };

    private static PaCapabilitiesSummaryDto BuildCapabilities(bool b2c = true, bool domesticPayment = true) => new()
    {
        PaName = "B2Brouter",
        SupportsB2cReporting = b2c,
        SupportsDomesticPaymentReporting = domesticPayment,
        SupportsInternationalPaymentReporting = false,
        SupportsB2bInvoicing = true,
        SupportsCreditNotes = true,
        SupportsTaxReportRetrieval = false,
        SupportsDocumentRetrieval = false,
        SupportsReportRectification = true,
        SupportsSelfBilling = false,
        MaxDocumentsPerRequest = 100,
    };

    private static ArchiveVerificationReport BuildReport(
        bool isIntact,
        bool isChainAnchored,
        bool isFullyVerified,
        string summary,
        string? firstBreakDetail = null)
    {
        var chain = new ArchiveIntegrityReport(isIntact, 12, [], firstBreakDetail);
        return new ArchiveVerificationReport(chain, [], isChainAnchored, isFullyVerified, summary);
    }
}
