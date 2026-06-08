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
/// Règle « Documents bloqués non traités » (SUP01b, F12 §5.2) : seuil par défaut 5 jours, surcharge tenant
/// (CFG02), âge dérivé du <c>LastUpdateUtc</c> du document le plus ancien en état Blocked. (INV-SUPERVISION-011)
/// </summary>
public sealed class BlockedDocumentsAlertRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static AlertEvaluationContext Context => new("acme", Now);

    [Fact]
    public void Rule_Key_And_Severity_Are_Stable()
    {
        var rule = new BlockedDocumentsAlertRule(new FakeDocumentQueries(), new FakeTenantSettingsQueries());

        rule.RuleKey.Should().Be("documents.blocked");
        rule.Severity.Should().Be(AlertSeverity.Warning);
    }

    [Fact]
    public async Task Fires_When_Oldest_Blocked_Exceeds_Default_Threshold()
    {
        var documents = new FakeDocumentQueries();
        documents.SetOldestInState("Blocked", RuleAlertTestData.Document("F-2026-001", "Blocked", Now.AddDays(-6)));
        var rule = new BlockedDocumentsAlertRule(documents, new FakeTenantSettingsQueries());

        var evaluation = await rule.EvaluateAsync(Context);

        evaluation.IsFiring.Should().BeTrue();
        evaluation.Detail.Should().Contain("F-2026-001").And.Contain("bloqué");
    }

    [Fact]
    public async Task Does_Not_Fire_Within_Threshold()
    {
        var documents = new FakeDocumentQueries();
        documents.SetOldestInState("Blocked", RuleAlertTestData.Document("F-2026-002", "Blocked", Now.AddDays(-4)));
        var rule = new BlockedDocumentsAlertRule(documents, new FakeTenantSettingsQueries());

        (await rule.EvaluateAsync(Context)).IsFiring.Should().BeFalse();
    }

    [Fact]
    public async Task Boundary_Exactly_Five_Days_Does_Not_Fire()
    {
        var documents = new FakeDocumentQueries();
        documents.SetOldestInState("Blocked", RuleAlertTestData.Document("F-2026-003", "Blocked", Now.AddDays(-5)));
        var rule = new BlockedDocumentsAlertRule(documents, new FakeTenantSettingsQueries());

        (await rule.EvaluateAsync(Context)).IsFiring.Should().BeFalse();
    }

    [Fact]
    public async Task No_Blocked_Documents_Clears()
    {
        var rule = new BlockedDocumentsAlertRule(new FakeDocumentQueries(), new FakeTenantSettingsQueries());

        (await rule.EvaluateAsync(Context)).IsFiring.Should().BeFalse();
    }

    [Fact]
    public async Task Tenant_Override_Tightens_Threshold()
    {
        var documents = new FakeDocumentQueries();
        documents.SetOldestInState("Blocked", RuleAlertTestData.Document("F-2026-004", "Blocked", Now.AddDays(-2)));
        var tenantSettings = new FakeTenantSettingsQueries(
            companyId: Guid.NewGuid(),
            thresholds: RuleAlertTestData.Thresholds(blockedDocumentsDays: 1));
        var rule = new BlockedDocumentsAlertRule(documents, tenantSettings);

        (await rule.EvaluateAsync(Context)).IsFiring.Should().BeTrue();
    }

    [Fact]
    public async Task Default_Used_When_Company_Present_But_No_Thresholds()
    {
        // Garde-fou partagé (DocumentStateAgeAlertRule) : company existe mais aucun seuil → défaut produit (5 j).
        var documents = new FakeDocumentQueries();
        documents.SetOldestInState("Blocked", RuleAlertTestData.Document("F-2026-005", "Blocked", Now.AddDays(-6)));
        var tenantSettings = new FakeTenantSettingsQueries(companyId: Guid.NewGuid(), thresholds: null);
        var rule = new BlockedDocumentsAlertRule(documents, tenantSettings);

        (await rule.EvaluateAsync(Context)).IsFiring.Should().BeTrue();
    }
}
