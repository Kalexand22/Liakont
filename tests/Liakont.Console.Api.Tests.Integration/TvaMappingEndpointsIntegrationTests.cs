namespace Liakont.Console.Api.Tests.Integration;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

/// <summary>
/// Tests d'intégration in-process des endpoints de paramétrage de la table TVA (API04), sur le harness
/// HTTP de la console (API01a). Vérifie : permissions (lecture <c>liakont.read</c> ; édition
/// <c>liakont.settings</c> — un utilisateur « actions » ne peut PAS éditer), délégation au moteur TVA05
/// (ajout/modification/suppression de règle, invalidation automatique de la validation, journal),
/// validation, et scoping par société/tenant (CLAUDE.md n°9). Les MUTATIONS portent sur un TENANT DÉDIÉ
/// (<see cref="ConsoleApiFactory.TenantApi04"/>) pour ne pas polluer l'état lu par les autres suites.
/// </summary>
[Collection(ConsoleApiCollectionFixture.Name)]
public sealed class TvaMappingEndpointsIntegrationTests
{
    private const string BasePath = "/api/v1/settings/tva-mapping";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ConsoleApiFactory _factory;

    public TvaMappingEndpointsIntegrationTests(ConsoleApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetTvaMapping_Without_Authentication_Returns_401()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantApi04);
        var response = await client.GetAsync(BasePath);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetTvaMapping_Without_Read_Permission_Returns_403()
    {
        using var client = _factory.CreateClient(
            ConsoleApiFactory.TenantApi04, ConsoleApiFactory.NoPermissionUserId, ConsoleApiFactory.TenantApi04CompanyId);
        var response = await client.GetAsync(BasePath);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetTvaMapping_As_Reader_Returns_Configured_Table()
    {
        using var client = ReaderClient();

        var view = await GetViewAsync(client);

        view.Table.Should().NotBeNull();
        view.Table!.Rules.Should().Contain(r => r.SourceRegimeCode == "REGIME-A" && r.Part == "Adjudication");
    }

    [Fact]
    public async Task GetTvaMapping_Is_Company_Scoped()
    {
        // Société sans table dans la base du tenant : la lecture est scopée par company_id (CLAUDE.md n°9).
        using var client = _factory.CreateClient(
            ConsoleApiFactory.TenantApi04, ConsoleApiFactory.ReaderUserId, Guid.NewGuid());
        var view = await GetViewAsync(client);
        view.Table.Should().BeNull();
    }

    [Fact]
    public async Task GetTvaMapping_Tenant_Isolation_Empty_Table_In_Other_Tenant()
    {
        // Le tenant B n'a aucune table TVA : la table du tenant API04 ne fuit jamais (database-per-tenant).
        using var client = _factory.CreateClient(
            ConsoleApiFactory.TenantB, ConsoleApiFactory.ReaderUserId, ConsoleApiFactory.TenantBCompanyId);
        var view = await GetViewAsync(client);
        view.Table.Should().BeNull();
    }

    [Fact]
    public async Task AddRule_Without_Authentication_Returns_401()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantApi04);
        var response = await client.PostAsJsonAsync($"{BasePath}/rules", NewRule("REGIME-X"));
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AddRule_As_Actions_User_Without_Settings_Returns_403()
    {
        // Un utilisateur « actions » (sans liakont.settings) ne peut PAS éditer la table TVA (acceptance API04).
        using var client = _factory.CreateClient(
            ConsoleApiFactory.TenantApi04, ConsoleApiFactory.OperatorUserId, ConsoleApiFactory.TenantApi04CompanyId);
        var response = await client.PostAsJsonAsync($"{BasePath}/rules", NewRule("REGIME-X"));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ValidateMapping_As_Reader_Without_Settings_Returns_403()
    {
        using var client = ReaderClient();
        var response = await client.PostAsync($"{BasePath}/validate", content: null);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Edit_Lifecycle_As_Settings_User_Goes_Through_Tva05_Engine()
    {
        // Cycle complet auto-restaurant sur le tenant dédié : ajout → invalidation + journal → modification
        // → suppression → re-validation. Laisse la table re-validée (état initial) pour rester indépendant
        // de l'ordre d'exécution des autres tests de cette classe.
        using var client = SettingsClient();
        const string code = "REGIME-LIFECYCLE";
        const string part = "Frais";

        // 1. Ajout d'une règle → invalide la validation + journal AddRule.
        var add = await client.PostAsJsonAsync($"{BasePath}/rules", NewRule(code, part));
        add.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterAdd = await GetViewAsync(client);
        afterAdd.Table.Should().NotBeNull();
        afterAdd.Table!.IsValidated.Should().BeFalse("toute mutation repasse la table « non validée » (TVA05 §2)");
        afterAdd.Table.Rules.Should().Contain(r => r.SourceRegimeCode == code && r.Part == part);
        afterAdd.ChangeLog.Should().Contain(
            e => e.ChangeType == "AddRule" && e.SourceRegimeCode == code,
            "toute mutation est journalisée (TVA05 §3)");

        // 2. Modification de la règle (libellé + taux) — clé inchangée.
        var update = await client.PutAsJsonAsync(
            $"{BasePath}/rules/{code}/{part}",
            new { sourceRegimeCode = code, part, category = "S", rateMode = "Fixed", rateValue = 5.5m, label = "Frais réduit" });
        update.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterUpdate = await GetViewAsync(client);
        afterUpdate.Table!.Rules.Single(r => r.SourceRegimeCode == code).Label.Should().Be("Frais réduit");

        // 3. Suppression de la règle.
        var delete = await client.DeleteAsync($"{BasePath}/rules/{code}/{part}");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterDelete = await GetViewAsync(client);
        afterDelete.Table!.Rules.Should().NotContain(r => r.SourceRegimeCode == code);

        // 4. Re-validation → la table redevient validée par l'opérateur courant.
        var validate = await client.PostAsync($"{BasePath}/validate", content: null);
        validate.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterValidate = await GetViewAsync(client);
        afterValidate.Table!.IsValidated.Should().BeTrue();
        afterValidate.Table.ValidatedBy.Should().Be(ConsoleApiFactory.SettingsUserId.ToString());
    }

    [Fact]
    public async Task UpdateRule_Route_Body_Mismatch_Returns_400()
    {
        using var client = SettingsClient();

        // Clé de l'URL ≠ clé du corps : refusé (on n'édite que la règle désignée par l'URL).
        var response = await client.PutAsJsonAsync(
            $"{BasePath}/rules/REGIME-A/Adjudication",
            new { sourceRegimeCode = "REGIME-B", part = "Adjudication", category = "S", rateMode = "Fixed", rateValue = 20m });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static object NewRule(string code, string part = "Frais") => new
    {
        sourceRegimeCode = code,
        part,
        category = "S",
        rateMode = "Fixed",
        rateValue = 10m,
    };

    private static async Task<ViewResponse> GetViewAsync(HttpClient client)
    {
        var response = await client.GetAsync(BasePath);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var view = await response.Content.ReadFromJsonAsync<ViewResponse>(JsonOptions);
        return view!;
    }

    private HttpClient ReaderClient() => _factory.CreateClient(
        ConsoleApiFactory.TenantApi04, ConsoleApiFactory.ReaderUserId, ConsoleApiFactory.TenantApi04CompanyId);

    private HttpClient SettingsClient() => _factory.CreateClient(
        ConsoleApiFactory.TenantApi04, ConsoleApiFactory.SettingsUserId, ConsoleApiFactory.TenantApi04CompanyId);

    private sealed record ViewResponse(TableDto? Table, List<ChangeLogDto> ChangeLog);

    private sealed record TableDto(
        Guid Id,
        Guid CompanyId,
        string MappingVersion,
        string? ValidatedBy,
        bool IsValidated,
        string DefaultBehavior,
        List<RuleDto> Rules);

    private sealed record RuleDto(string SourceRegimeCode, string? Label, string Part, string Category, string? Vatex, string RateMode, decimal? RateValue);

    private sealed record ChangeLogDto(Guid Id, string ChangeType, string? SourceRegimeCode, string? Part, string MappingVersion, Guid OperatorId, DateTimeOffset OccurredAt);
}
