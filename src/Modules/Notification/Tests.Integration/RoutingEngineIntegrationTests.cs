namespace Stratum.Modules.Notification.Tests.Integration;

using System.Text.Json;
using FluentAssertions;
using Stratum.Modules.Notification.Domain.Entities;
using Stratum.Modules.Notification.Domain.Services;
using Stratum.Modules.Notification.Domain.ValueObjects;
using Stratum.Modules.Notification.Tests.Integration.Fixtures;
using Xunit;

[Collection("NotificationIntegration")]
public sealed class RoutingEngineIntegrationTests
{
    private readonly NotificationDatabaseFixture _fixture;

    public RoutingEngineIntegrationTests(NotificationDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Evaluator_With_BesoinTechnique_Should_Match_Technique_And_CatchAll()
    {
        var rules = BuildSeededRules();

        var data = new Dictionary<string, JsonElement>
        {
            ["besoin_technique"] = JsonSerializer.SerializeToElement(true),
        };

        var matches = RoutingEvaluator.Evaluate(rules, data);

        matches.Should().Contain(m => m.ServiceCode == "technique");
        matches.Should().Contain(m => m.ServiceCode == "gestion-salles");
    }

    [Fact]
    public void Evaluator_With_FermetureVoirie_Should_Match_Voirie()
    {
        var rules = BuildSeededRules();

        var data = new Dictionary<string, JsonElement>
        {
            ["fermeture_voirie"] = JsonSerializer.SerializeToElement(true),
        };

        var matches = RoutingEvaluator.Evaluate(rules, data);

        matches.Should().Contain(m => m.ServiceCode == "voirie");
    }

    [Fact]
    public void Evaluator_With_PublicEvent_Should_Match_Communication()
    {
        var rules = BuildSeededRules();

        var data = new Dictionary<string, JsonElement>
        {
            ["public"] = JsonSerializer.SerializeToElement(true),
        };

        var matches = RoutingEvaluator.Evaluate(rules, data);

        matches.Should().Contain(m => m.ServiceCode == "communication");
    }

    [Fact]
    public void Evaluator_With_NoData_Should_Match_CatchAll_Only()
    {
        var rules = BuildSeededRules();
        var data = new Dictionary<string, JsonElement>();

        var matches = RoutingEvaluator.Evaluate(rules, data);

        matches.Should().Contain(m => m.ServiceCode == "gestion-salles");
        matches.Should().NotContain(m => m.ServiceCode == "technique");
        matches.Should().NotContain(m => m.ServiceCode == "voirie");
        matches.Should().NotContain(m => m.ServiceCode == "police");
    }

    [Fact]
    public void Evaluator_With_HighAffluence_Should_Match_Police()
    {
        var rules = BuildSeededRules();

        var data = new Dictionary<string, JsonElement>
        {
            ["affluence"] = JsonSerializer.SerializeToElement(300),
        };

        var matches = RoutingEvaluator.Evaluate(rules, data);

        matches.Should().Contain(m => m.ServiceCode == "police");
    }

    [Fact]
    public void Evaluator_With_PayantEvent_Should_Match_Finances()
    {
        var rules = BuildSeededRules();

        var data = new Dictionary<string, JsonElement>
        {
            ["payant"] = JsonSerializer.SerializeToElement(true),
        };

        var matches = RoutingEvaluator.Evaluate(rules, data);

        matches.Should().Contain(m => m.ServiceCode == "finances");
    }

    [Fact]
    public void Evaluator_Results_Should_Be_Ordered_By_Priority()
    {
        var rules = BuildSeededRules();

        var data = new Dictionary<string, JsonElement>
        {
            ["besoin_technique"] = JsonSerializer.SerializeToElement(true),
            ["public"] = JsonSerializer.SerializeToElement(true),
        };

        var matches = RoutingEvaluator.Evaluate(rules, data);

        matches.Should().BeInAscendingOrder(m => m.Priority);
    }

    /// <summary>
    /// Builds RoutingRule domain objects matching the seeded V010 migration data,
    /// used for pure in-memory evaluation tests.
    /// </summary>
    private static List<RoutingRule> BuildSeededRules()
    {
        return
        [
            CreateRule("gestion-salles", "Toute demande d'espace", "gestion-salles", "salles@commune.local", [], 100),
            CreateRule("technique", "Branchements, materiel ou sono", "technique", "technique@commune.local",
                [RoutingCondition.Leaf("besoin_technique", "eq", JsonSerializer.SerializeToElement(true))], 50),
            CreateRule("voirie", "Fermeture de voirie requise", "voirie", "voirie@commune.local",
                [RoutingCondition.Leaf("fermeture_voirie", "eq", JsonSerializer.SerializeToElement(true))], 40),
            CreateRule("police", "Affluence importante ou nocturne public", "police", "police@commune.local",
                [RoutingCondition.Compound("or",
                [
                    RoutingCondition.Leaf("affluence", "gt", JsonSerializer.SerializeToElement(200)),
                    RoutingCondition.Compound("and",
                    [
                        RoutingCondition.Leaf("public", "eq", JsonSerializer.SerializeToElement(true)),
                        RoutingCondition.Leaf("nocturne", "eq", JsonSerializer.SerializeToElement(true)),
                    ]),
                ])], 30),
            CreateRule("communication", "Evenement public", "communication", "communication@commune.local",
                [RoutingCondition.Leaf("public", "eq", JsonSerializer.SerializeToElement(true))], 60),
            CreateRule("finances", "Evenement payant ou avec caution", "finances", "finances@commune.local",
                [RoutingCondition.Compound("or",
                [
                    RoutingCondition.Leaf("payant", "eq", JsonSerializer.SerializeToElement(true)),
                    RoutingCondition.Leaf("caution", "eq", JsonSerializer.SerializeToElement(true)),
                ])], 70),
        ];
    }

    private static RoutingRule CreateRule(
        string code,
        string name,
        string serviceCode,
        string email,
        IReadOnlyList<RoutingCondition> conditions,
        int priority)
    {
        return RoutingRule.Reconstitute(
            Guid.NewGuid(),
            code,
            name,
            "reservation",
            serviceCode,
            RecipientType.ServiceEmail,
            email,
            conditions,
            priority,
            true,
            null,
            DateTimeOffset.UtcNow,
            null);
    }
}
