namespace Liakont.Console.Api.Tests.Integration;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

/// <summary>
/// Tests d'intégration in-process des endpoints de lecture Documents de la console (API01a) :
/// permission <c>liakont.read</c> (401/403), isolation tenant (A≠B, physique), liste paginée + filtres +
/// compteurs par état, et détail (motif de blocage, pivot transmis, référence d'archive).
/// </summary>
[Collection(ConsoleApiCollectionFixture.Name)]
public sealed class DocumentsEndpointsIntegrationTests
{
    private const string DocumentsPath = "/api/v1/documents";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ConsoleApiFactory _factory;

    public DocumentsEndpointsIntegrationTests(ConsoleApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetDocuments_Without_Authentication_Returns_401()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantA);

        var response = await client.GetAsync(DocumentsPath);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetDocuments_Without_Read_Permission_Returns_403()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantA, ConsoleApiFactory.NoPermissionUserId);

        var response = await client.GetAsync(DocumentsPath);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetDocuments_As_Reader_Returns_Tenant_Scoped_List_With_State_Counts()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantA, ConsoleApiFactory.ReaderUserId);

        var result = await GetListAsync(client, DocumentsPath);

        result.TotalCount.Should().Be(3, "le tenant A a exactement 3 documents seedés");
        result.Items.Should().HaveCount(3);
        result.Items.Should().OnlyContain(i => i.DocumentNumber.StartsWith("FA-A-", StringComparison.Ordinal)
            || i.DocumentNumber.StartsWith("AV-A-", StringComparison.Ordinal));
        result.CountsByState.Should().Contain(new KeyValuePair<string, int>("ReadyToSend", 1));
        result.CountsByState.Should().Contain(new KeyValuePair<string, int>("Blocked", 1));
        result.CountsByState.Should().Contain(new KeyValuePair<string, int>("Issued", 1));
    }

    [Fact]
    public async Task GetDocuments_Filter_By_State_Returns_Only_Matching()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantA, ConsoleApiFactory.ReaderUserId);

        var result = await GetListAsync(client, $"{DocumentsPath}?state=Blocked");

        result.TotalCount.Should().Be(1);
        result.Items.Should().ContainSingle(i => i.State == "Blocked");
    }

    [Fact]
    public async Task GetDocuments_Filter_By_Type_Returns_Only_Matching()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantA, ConsoleApiFactory.ReaderUserId);

        var result = await GetListAsync(client, $"{DocumentsPath}?type=credit_note");

        result.TotalCount.Should().Be(1);
        result.Items.Should().ContainSingle(i => i.DocumentNumber == "AV-A-003");
    }

    [Fact]
    public async Task GetDocuments_Search_Matches_Document_Number()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantA, ConsoleApiFactory.ReaderUserId);

        var result = await GetListAsync(client, $"{DocumentsPath}?search=AV-A");

        result.TotalCount.Should().Be(1);
        result.Items.Should().ContainSingle(i => i.DocumentNumber == "AV-A-003");
    }

    [Fact]
    public async Task GetDocuments_Pagination_Bounds_Page_Size()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantA, ConsoleApiFactory.ReaderUserId);

        var result = await GetListAsync(client, $"{DocumentsPath}?page=1&pageSize=2");

        result.Items.Should().HaveCount(2, "la page est limitée à 2 éléments");
        result.TotalCount.Should().Be(3, "le total reflète tous les documents correspondant aux filtres");
        result.PageSize.Should().Be(2);
    }

    [Fact]
    public async Task GetDocumentById_Blocked_Returns_Blocking_Reason()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantA, ConsoleApiFactory.ReaderUserId);

        var detail = await GetDetailAsync(client, $"{DocumentsPath}/{ConsoleApiFactory.TenantADocBlockedId}");

        detail.Document.State.Should().Be("Blocked");
        detail.BlockingReason.Should().Be(ConsoleApiFactory.BlockedReasonText);
    }

    [Fact]
    public async Task GetDocumentById_Issued_Returns_Archive_Reference_And_Pivot()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantA, ConsoleApiFactory.ReaderUserId);

        var detail = await GetDetailAsync(client, $"{DocumentsPath}/{ConsoleApiFactory.TenantADocIssuedId}");

        detail.Document.State.Should().Be("Issued");
        detail.ArchiveIntegrity.Should().Be("Archived");
        detail.Archive.Should().NotBeNull();
        detail.Archive!.PackagePath.Should().Be("vault/tenant-a/AV-A-003.zip");
        detail.PivotSnapshotJson.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetDocumentById_Ready_Without_Archive_Reports_NotArchived()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantA, ConsoleApiFactory.ReaderUserId);

        var detail = await GetDetailAsync(client, $"{DocumentsPath}/{ConsoleApiFactory.TenantADocReadyId}");

        detail.ArchiveIntegrity.Should().Be("NotArchived");
        detail.Archive.Should().BeNull();
    }

    [Fact]
    public async Task GetDocuments_Is_Tenant_Isolated()
    {
        using var clientB = _factory.CreateClient(ConsoleApiFactory.TenantB, ConsoleApiFactory.ReaderUserId);

        var result = await GetListAsync(clientB, DocumentsPath);

        result.TotalCount.Should().Be(1, "le tenant B n'a qu'un document seedé");
        result.Items.Should().ContainSingle(i => i.DocumentNumber == "FA-B-001");
    }

    [Fact]
    public async Task GetDocumentById_From_Other_Tenant_Is_Not_Found()
    {
        // Le document existe dans le tenant A ; lu avec le contexte du tenant B, il est introuvable
        // (bases physiquement distinctes — isolation tenant, CLAUDE.md n°9).
        using var clientB = _factory.CreateClient(ConsoleApiFactory.TenantB, ConsoleApiFactory.ReaderUserId);

        var response = await clientB.GetAsync($"{DocumentsPath}/{ConsoleApiFactory.TenantADocIssuedId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task<ListResponse> GetListAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ListResponse>(JsonOptions);
        result.Should().NotBeNull();
        return result!;
    }

    private static async Task<DetailResponse> GetDetailAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<DetailResponse>(JsonOptions);
        result.Should().NotBeNull();
        return result!;
    }

    private sealed record ListResponse(
        List<SummaryItem> Items,
        int Page,
        int PageSize,
        int TotalCount,
        Dictionary<string, int> CountsByState);

    private sealed record SummaryItem(
        Guid Id,
        string DocumentNumber,
        string DocumentType,
        string State,
        decimal TotalGross);

    private sealed record DetailResponse(
        DocumentCore Document,
        string? BlockingReason,
        string? PivotSnapshotJson,
        ArchiveReference? Archive,
        string ArchiveIntegrity);

    private sealed record DocumentCore(Guid Id, string DocumentNumber, string State);

    private sealed record ArchiveReference(
        string PackagePath,
        string PackageHash,
        string ChainHash,
        DateTimeOffset ArchivedUtc);
}
