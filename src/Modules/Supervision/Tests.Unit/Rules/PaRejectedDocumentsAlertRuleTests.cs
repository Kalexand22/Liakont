namespace Liakont.Modules.Supervision.Tests.Unit.Rules;

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Supervision.Application;
using Liakont.Modules.Supervision.Application.Rules;
using Liakont.Modules.Supervision.Domain;
using Liakont.Modules.Supervision.Tests.Unit.Doubles;
using Xunit;

/// <summary>
/// Règle « Rejets PA non traités » (SUP01b, F12 §5.2) : seuil par défaut 2 jours (plus serré, gravité
/// critique), surcharge tenant (CFG02), âge dérivé du document le plus ancien en RejectedByPa. (INV-SUPERVISION-012)
/// </summary>
public sealed class PaRejectedDocumentsAlertRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static AlertEvaluationContext Context => new("acme", Now);

    [Fact]
    public void Rule_Key_And_Severity_Are_Stable()
    {
        var rule = new PaRejectedDocumentsAlertRule(new FakeDocumentQueries(), new FakeTenantSettingsQueries());

        rule.RuleKey.Should().Be("documents.pa_rejected");
        rule.Severity.Should().Be(AlertSeverity.Critical);
    }

    [Fact]
    public async Task Fires_When_Oldest_Rejection_Exceeds_Default_Threshold()
    {
        var documents = new FakeDocumentQueries();
        documents.SetOldestInState("RejectedByPa", RuleAlertTestData.Document("F-2026-010", "RejectedByPa", Now.AddDays(-3)));
        var rule = new PaRejectedDocumentsAlertRule(documents, new FakeTenantSettingsQueries());

        var evaluation = await rule.EvaluateAsync(Context);

        evaluation.IsFiring.Should().BeTrue();
        evaluation.Detail.Should().Contain("F-2026-010").And.Contain("rejeté par la Plateforme Agréée");
    }

    [Fact]
    public async Task Does_Not_Fire_Within_Threshold()
    {
        var documents = new FakeDocumentQueries();
        documents.SetOldestInState("RejectedByPa", RuleAlertTestData.Document("F-2026-011", "RejectedByPa", Now.AddDays(-1)));
        var rule = new PaRejectedDocumentsAlertRule(documents, new FakeTenantSettingsQueries());

        (await rule.EvaluateAsync(Context)).IsFiring.Should().BeFalse();
    }

    [Fact]
    public async Task Boundary_Exactly_Two_Days_Does_Not_Fire()
    {
        var documents = new FakeDocumentQueries();
        documents.SetOldestInState("RejectedByPa", RuleAlertTestData.Document("F-2026-012", "RejectedByPa", Now.AddDays(-2)));
        var rule = new PaRejectedDocumentsAlertRule(documents, new FakeTenantSettingsQueries());

        (await rule.EvaluateAsync(Context)).IsFiring.Should().BeFalse();
    }

    [Fact]
    public async Task No_Rejections_Clears()
    {
        var rule = new PaRejectedDocumentsAlertRule(new FakeDocumentQueries(), new FakeTenantSettingsQueries());

        (await rule.EvaluateAsync(Context)).IsFiring.Should().BeFalse();
    }

    [Fact]
    public async Task Tenant_Override_Relaxes_Threshold()
    {
        // Seuil tenant à 7 j : un rejet vieux de 3 j ne déclenche pas, là où le défaut (2 j) déclencherait.
        var documents = new FakeDocumentQueries();
        documents.SetOldestInState("RejectedByPa", RuleAlertTestData.Document("F-2026-013", "RejectedByPa", Now.AddDays(-3)));
        var tenantSettings = new FakeTenantSettingsQueries(
            companyId: Guid.NewGuid(),
            thresholds: RuleAlertTestData.Thresholds(paRejectionsDays: 7));
        var rule = new PaRejectedDocumentsAlertRule(documents, tenantSettings);

        (await rule.EvaluateAsync(Context)).IsFiring.Should().BeFalse();
    }

    [Fact]
    public async Task Default_Used_When_Company_Present_But_No_Thresholds()
    {
        // Garde-fou partagé (DocumentStateAgeAlertRule) : company existe mais aucun seuil → défaut produit (2 j).
        var documents = new FakeDocumentQueries();
        documents.SetOldestInState("RejectedByPa", RuleAlertTestData.Document("F-2026-014", "RejectedByPa", Now.AddDays(-3)));
        var tenantSettings = new FakeTenantSettingsQueries(companyId: Guid.NewGuid(), thresholds: null);
        var rule = new PaRejectedDocumentsAlertRule(documents, tenantSettings);

        (await rule.EvaluateAsync(Context)).IsFiring.Should().BeTrue();
    }
}
