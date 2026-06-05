namespace Liakont.PaClients.B2Brouter.Tests.Unit;

using System.Net;
using System.Text.Json;
using FluentAssertions;
using Liakont.Modules.Transmission.Contracts;
using Xunit;

/// <summary>
/// Tests de LECTURE de PAB03 (tax reports DGFiP, réglage idempotent, compte) avec handler HTTP routé.
/// Vérifient les FAITS F05 §2 (endpoints exacts, xml_base64 absent = « pas encore généré »), la
/// correction fiscale (un échec serveur LÈVE, ne retourne jamais une liste/valeur vide — CLAUDE.md n°3)
/// et l'idempotence d'<c>EnsureTaxReportSettingAsync</c> (GET → POST/PATCH si écart, no-op si identique).
/// Valeurs FICTIVES (aucune donnée client — CLAUDE.md n°7).
/// </summary>
public sealed class B2BrouterClientReadTests
{
    private const string Account = "ACC-42";
    private const string TaxReportsPath = "/accounts/ACC-42/tax_reports.json";
    private const string AccountPath = "/accounts/ACC-42.json";
    private const string SettingPath = "/accounts/ACC-42/tax_report_settings/dgfip.json";

    [Fact]
    public async Task ListTaxReports_Gets_The_Account_Tax_Reports_Endpoint_And_Maps_Reports()
    {
        const string body = """
            [
              {"id":"TR-1","type":"dgfip","transport":"AS4","state":"sent","has_errors":false},
              {"id":"TR-2","type":"dgfip","state":"registered","xml_base64":"PHhtbD4=","has_errors":false}
            ]
            """;
        var handler = new RoutingHttpMessageHandler().On(HttpMethod.Get, TaxReportsPath, HttpStatusCode.OK, body);
        var client = CreateClient(handler);

        var reports = await client.ListTaxReportsAsync();

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Uri.AbsolutePath.Should().Be(TaxReportsPath);
        reports.Should().HaveCount(2);
        reports[0].Id.Should().Be("TR-1");
        reports[0].State.Should().Be(PaTaxReportState.Sent);
        reports[0].Transport.Should().Be("AS4");
        reports[0].XmlBase64.Should().BeNull("le tax report « sent » n'a pas encore son ledger généré (F05 §2)");
        reports[1].State.Should().Be(PaTaxReportState.Registered);
        reports[1].XmlBase64.Should().Be("PHhtbD4=");
    }

    [Fact]
    public async Task ListTaxReports_Empty_Array_Is_An_Empty_List()
    {
        var handler = new RoutingHttpMessageHandler().On(HttpMethod.Get, TaxReportsPath, HttpStatusCode.OK, "[]");
        var client = CreateClient(handler);

        var reports = await client.ListTaxReportsAsync();

        reports.Should().BeEmpty();
    }

    [Fact]
    public async Task ListTaxReports_Server_Error_Throws_Rather_Than_Returning_An_Empty_List()
    {
        var handler = new RoutingHttpMessageHandler().On(HttpMethod.Get, TaxReportsPath, HttpStatusCode.ServiceUnavailable);
        var client = CreateClient(handler);

        var act = () => client.ListTaxReportsAsync();

        await act.Should().ThrowAsync<HttpRequestException>(
            "un échec serveur ne doit JAMAIS passer pour « aucun tax report » (mensonge fiscal — CLAUDE.md n°3)");
    }

    [Fact]
    public async Task ListTaxReports_Caller_Cancellation_Does_Not_Call_The_Pa()
    {
        var handler = new RoutingHttpMessageHandler().On(HttpMethod.Get, TaxReportsPath, HttpStatusCode.OK, "[]");
        var client = CreateClient(handler);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => client.ListTaxReportsAsync(cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetTaxReport_Gets_The_TaxReports_Endpoint_Not_Under_Accounts()
    {
        const string body = """{"id":"TR-9","type":"dgfip","state":"registered","xml_base64":"PHhtbD4=","has_errors":false}""";
        var handler = new RoutingHttpMessageHandler().On(HttpMethod.Get, "/tax_reports/TR-9.json", HttpStatusCode.OK, body);
        var client = CreateClient(handler);

        var report = await client.GetTaxReportAsync("TR-9");

        handler.Requests[0].Uri.AbsolutePath.Should().Be("/tax_reports/TR-9.json", "F05 §2 : GET /tax_reports/<id>.json (PAS sous /accounts)");
        report.XmlBase64.Should().Be("PHhtbD4=");
        report.RawResponse.Should().NotBeNullOrEmpty("la réponse brute est conservée pour l'audit (F06/DR6)");
    }

    [Fact]
    public async Task GetTaxReport_Without_Xml_Is_Treated_As_Not_Yet_Generated()
    {
        const string body = """{"id":"TR-1","type":"dgfip","state":"new"}""";
        var handler = new RoutingHttpMessageHandler().On(HttpMethod.Get, "/tax_reports/TR-1.json", HttpStatusCode.OK, body);
        var client = CreateClient(handler);

        var report = await client.GetTaxReportAsync("TR-1");

        report.State.Should().Be(PaTaxReportState.New);
        report.XmlBase64.Should().BeNull("xml_base64 absent = « pas encore généré », pas une erreur (acceptance PAB03)");
        report.HasErrors.Should().BeFalse();
    }

    [Fact]
    public async Task GetTaxReport_Server_Error_Throws()
    {
        var handler = new RoutingHttpMessageHandler().On(HttpMethod.Get, "/tax_reports/TR-1.json", HttpStatusCode.InternalServerError);
        var client = CreateClient(handler);

        var act = () => client.GetTaxReportAsync("TR-1");

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task GetAccountInfo_Gets_The_Account_Endpoint_And_Maps_Transactions()
    {
        const string body = """{"id":"ACC-42","transactions_count":12,"transactions_limit":1000}""";
        var handler = new RoutingHttpMessageHandler().On(HttpMethod.Get, AccountPath, HttpStatusCode.OK, body);
        var client = CreateClient(handler);

        var info = await client.GetAccountInfoAsync();

        handler.Requests[0].Uri.AbsolutePath.Should().Be(AccountPath);
        info.AccountId.Should().Be("ACC-42");
        info.TransactionsCount.Should().Be(12);
        info.TransactionsLimit.Should().Be(1000);
    }

    [Fact]
    public async Task GetAccountInfo_Missing_Counts_Are_Null_Not_Zero()
    {
        const string body = """{"id":"ACC-42"}""";
        var handler = new RoutingHttpMessageHandler().On(HttpMethod.Get, AccountPath, HttpStatusCode.OK, body);
        var client = CreateClient(handler);

        var info = await client.GetAccountInfoAsync();

        info.TransactionsCount.Should().BeNull("un compteur absent reste null, jamais 0 qui masquerait la donnée (module-rules §9)");
        info.TransactionsLimit.Should().BeNull();
    }

    [Fact]
    public async Task GetTaxReportSetting_404_Is_An_Empty_Setting_Not_An_Error()
    {
        // Aucune route → 404 (réglage pas encore créé).
        var handler = new RoutingHttpMessageHandler();
        var client = CreateClient(handler);

        var setting = await client.GetTaxReportSettingAsync();

        setting.NafCode.Should().BeNull();
        setting.StartDate.Should().BeNull();
        setting.CinScheme.Should().BeNull();
    }

    [Fact]
    public async Task GetTaxReportSetting_Maps_Fields_Including_Cin_Scheme_And_Start_Date()
    {
        const string body = """{"naf_code":"62","start_date":"2026-09-01","type_operation":"B2C","enterprise_size":"PME","cin_scheme":"0002"}""";
        var handler = new RoutingHttpMessageHandler().On(HttpMethod.Get, SettingPath, HttpStatusCode.OK, body);
        var client = CreateClient(handler);

        var setting = await client.GetTaxReportSettingAsync();

        handler.Requests[0].Uri.AbsolutePath.Should().Be(SettingPath, "F05 §2 : variante DGFiP du réglage");
        setting.NafCode.Should().Be("62");
        setting.StartDate.Should().Be(new DateOnly(2026, 9, 1));
        setting.TypeOperation.Should().Be("B2C");
        setting.EnterpriseSize.Should().Be("PME");
        setting.CinScheme.Should().Be("0002", "le PA est assigné au niveau SIREN (scheme 0002) — F05 §2");
    }

    [Fact]
    public async Task Ensure_When_Absent_404_Creates_With_A_Wrapped_DGFiP_Payload()
    {
        // GET → 404 (pas de route GET) ; POST accepté.
        var handler = new RoutingHttpMessageHandler().On(HttpMethod.Post, SettingPath, HttpStatusCode.OK, "{}");
        var client = CreateClient(handler);

        await client.EnsureTaxReportSettingAsync(DesiredSetting());

        handler.Requests.Should().HaveCount(2, "GET (404) puis POST de création");
        handler.Requests[0].Method.Should().Be(HttpMethod.Get);
        handler.Requests[1].Method.Should().Be(HttpMethod.Post);
        handler.Requests[1].Uri.AbsolutePath.Should().Be(SettingPath);

        var setting = PostedSetting(handler.Requests[1].Body);
        setting.GetProperty("start_date").GetString().Should().Be("2026-09-01");
        setting.GetProperty("type_operation").GetString().Should().Be("B2C");
        setting.GetProperty("enterprise_size").GetString().Should().Be("PME");
        setting.GetProperty("naf_code").GetString().Should().Be("62");
        setting.GetProperty("cin_scheme").GetString().Should().Be("0002");
    }

    [Fact]
    public async Task Ensure_When_Already_Identical_Does_Not_Write()
    {
        const string current = """{"naf_code":"62","start_date":"2026-09-01","type_operation":"B2C","enterprise_size":"PME","cin_scheme":"0002"}""";
        var handler = new RoutingHttpMessageHandler().On(HttpMethod.Get, SettingPath, HttpStatusCode.OK, current);
        var client = CreateClient(handler);

        await client.EnsureTaxReportSettingAsync(DesiredSetting());

        handler.CallCount.Should().Be(1, "réglage déjà conforme → aucune écriture (idempotence F05 §2)");
        handler.Requests[0].Method.Should().Be(HttpMethod.Get);
    }

    [Fact]
    public async Task Ensure_When_Different_Patches()
    {
        const string current = """{"naf_code":"62","start_date":"2026-01-01","type_operation":"B2C","enterprise_size":"PME","cin_scheme":"0002"}""";
        var handler = new RoutingHttpMessageHandler()
            .On(HttpMethod.Get, SettingPath, HttpStatusCode.OK, current)
            .On(HttpMethod.Patch, SettingPath, HttpStatusCode.OK, "{}");
        var client = CreateClient(handler);

        await client.EnsureTaxReportSettingAsync(DesiredSetting());

        handler.Requests.Should().HaveCount(2, "GET puis PATCH (écart sur start_date)");
        handler.Requests[1].Method.Should().Be(HttpMethod.Patch);
    }

    [Fact]
    public async Task Ensure_Get_Server_Error_Throws_And_Does_Not_Write()
    {
        var handler = new RoutingHttpMessageHandler().On(HttpMethod.Get, SettingPath, HttpStatusCode.InternalServerError);
        var client = CreateClient(handler);

        var act = () => client.EnsureTaxReportSettingAsync(DesiredSetting());

        await act.Should().ThrowAsync<HttpRequestException>("un 5xx sur le GET ne doit pas déclencher une écriture aveugle");
        handler.CallCount.Should().Be(1, "seul le GET a eu lieu, aucune écriture");
    }

    [Fact]
    public async Task Ensure_Create_Post_Server_Error_Throws()
    {
        // GET → 404 (pas de route GET) ; POST → 503.
        var handler = new RoutingHttpMessageHandler().On(HttpMethod.Post, SettingPath, HttpStatusCode.ServiceUnavailable);
        var client = CreateClient(handler);

        var act = () => client.EnsureTaxReportSettingAsync(DesiredSetting());

        await act.Should().ThrowAsync<HttpRequestException>(
            "un échec serveur sur le POST de création ne doit jamais réussir silencieusement (CLAUDE.md n°3)");
    }

    [Fact]
    public async Task Ensure_Update_Patch_Server_Error_Throws()
    {
        const string current = """{"naf_code":"62","start_date":"2026-01-01","type_operation":"B2C","enterprise_size":"PME","cin_scheme":"0002"}""";
        var handler = new RoutingHttpMessageHandler()
            .On(HttpMethod.Get, SettingPath, HttpStatusCode.OK, current)
            .On(HttpMethod.Patch, SettingPath, HttpStatusCode.ServiceUnavailable);
        var client = CreateClient(handler);

        var act = () => client.EnsureTaxReportSettingAsync(DesiredSetting());

        await act.Should().ThrowAsync<HttpRequestException>(
            "un échec serveur sur le PATCH de mise à jour ne doit jamais réussir silencieusement (CLAUDE.md n°3)");
    }

    [Fact]
    public async Task Ensure_Null_Desired_Optional_Already_Set_On_Pa_Does_Not_Patch()
    {
        // La PA porte déjà naf_code ; la demande ne le fournit pas (null) → on ne le gère pas → pas de PATCH.
        const string current = """{"naf_code":"62","start_date":"2026-09-01","type_operation":"B2C","enterprise_size":"PME","cin_scheme":"0002"}""";
        var handler = new RoutingHttpMessageHandler().On(HttpMethod.Get, SettingPath, HttpStatusCode.OK, current);
        var desired = new PaTaxReportSettingRequest
        {
            // NafCode et CinScheme volontairement omis (null) : non gérés par ce tenant.
            StartDate = new DateOnly(2026, 9, 1),
            TypeOperation = "B2C",
            EnterpriseSize = "PME",
        };

        var client = CreateClient(handler);
        await client.EnsureTaxReportSettingAsync(desired);
        handler.CallCount.Should().Be(1, "optionnels non gérés (null) déjà posés côté PA → aucun PATCH (idempotence convergente)");
        handler.Requests[0].Method.Should().Be(HttpMethod.Get);
    }

    private static PaTaxReportSettingRequest DesiredSetting() => new()
    {
        NafCode = "62",
        StartDate = new DateOnly(2026, 9, 1),
        TypeOperation = "B2C",
        EnterpriseSize = "PME",
        CinScheme = "0002",
    };

    private static JsonElement PostedSetting(string? requestBody)
    {
        requestBody.Should().NotBeNullOrEmpty();
        using var doc = JsonDocument.Parse(requestBody!);
        return doc.RootElement.GetProperty("tax_report_setting").Clone();
    }

    private static B2BrouterClient CreateClient(RoutingHttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri(B2BrouterDefaults.StagingBaseUrl) };
        return new B2BrouterClient(http, new B2BrouterClientOptions(Account));
    }
}
