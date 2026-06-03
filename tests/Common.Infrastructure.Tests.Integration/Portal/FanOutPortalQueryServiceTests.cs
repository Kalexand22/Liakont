namespace Stratum.Common.Infrastructure.Tests.Integration.Portal;

using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Stratum.Common.Abstractions.Portal;
using Stratum.Common.Infrastructure.Portal;
using Xunit;

public sealed class FanOutPortalQueryServiceTests : IClassFixture<MultiTenantFixture>, IAsyncLifetime
{
    private readonly MultiTenantFixture _fixture;

    public FanOutPortalQueryServiceTests(MultiTenantFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        // Truncate tables before each test for isolation
        await TruncateTenantDataAsync(MultiTenantFixture.TenantA);
        await TruncateTenantDataAsync(MultiTenantFixture.TenantB);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetPublicEventsAsync_Should_Return_Public_Parties_From_Portal_Enabled_Tenants()
    {
        await _fixture.SeedPublicPartyAsync(MultiTenantFixture.TenantA, "Chorale du Lac", isPublic: true, portalEnabled: true);
        await _fixture.SeedPublicPartyAsync(MultiTenantFixture.TenantA, "Private Club", isPublic: false, portalEnabled: true);
        await _fixture.SeedPublicPartyAsync(MultiTenantFixture.TenantB, "Muni Festival", isPublic: true, portalEnabled: true);

        var service = CreateService();
        var result = await service.GetPublicEventsAsync(new PortalFilter(), page: 1, pageSize: 50, ct: default);

        result.TotalCount.Should().Be(2);
        result.Items.Should().Contain(e => e.Title == "Chorale du Lac" && e.TenantDisplayName == "Tenant A Assoc");
        result.Items.Should().Contain(e => e.Title == "Muni Festival" && e.TenantDisplayName == "Tenant B Muni");
        result.Items.Should().NotContain(e => e.Title == "Private Club");
    }

    [Fact]
    public async Task GetPublicEventsAsync_Should_Skip_Tenant_Without_Portal_Enabled()
    {
        // Tenant A: portal enabled, public party
        await _fixture.SeedPublicPartyAsync(MultiTenantFixture.TenantA, "Visible Org", isPublic: true, portalEnabled: true);

        // Tenant B: NO portal flag, public party — should be excluded
        await _fixture.SeedPublicPartyAsync(MultiTenantFixture.TenantB, "Hidden Org", isPublic: true, portalEnabled: false);

        var service = CreateService();
        var result = await service.GetPublicEventsAsync(new PortalFilter(), page: 1, pageSize: 50, ct: default);

        result.TotalCount.Should().Be(1);
        result.Items.Should().Contain(e => e.Title == "Visible Org");
        result.Items.Should().NotContain(e => e.Title == "Hidden Org");
    }

    [Fact]
    public async Task GetPublicEventsAsync_Should_Filter_By_TenantId()
    {
        await _fixture.SeedPublicPartyAsync(MultiTenantFixture.TenantA, "A Only Party", isPublic: true, portalEnabled: true);
        await _fixture.SeedPublicPartyAsync(MultiTenantFixture.TenantB, "B Only Party", isPublic: true, portalEnabled: true);

        var service = CreateService();
        var filter = new PortalFilter { TenantId = MultiTenantFixture.TenantA };
        var result = await service.GetPublicEventsAsync(filter, page: 1, pageSize: 50, ct: default);

        result.TotalCount.Should().Be(1);
        result.Items.Should().OnlyContain(e => e.TenantId == MultiTenantFixture.TenantA);
    }

    [Fact]
    public async Task GetPublicEventsAsync_Should_Filter_By_Keyword()
    {
        await _fixture.SeedPublicPartyAsync(MultiTenantFixture.TenantA, "UniqueKeywordOrg", isPublic: true, portalEnabled: true);
        await _fixture.SeedPublicPartyAsync(MultiTenantFixture.TenantA, "Other Org", isPublic: true, portalEnabled: true);

        var service = CreateService();
        var filter = new PortalFilter { Keyword = "UniqueKeyword" };
        var result = await service.GetPublicEventsAsync(filter, page: 1, pageSize: 50, ct: default);

        result.TotalCount.Should().Be(1);
        result.Items.Should().Contain(e => e.Title == "UniqueKeywordOrg");
    }

    [Fact]
    public async Task GetPublicEventsAsync_Should_Paginate_CrossTenant()
    {
        for (var i = 0; i < 5; i++)
        {
            await _fixture.SeedPublicPartyAsync(MultiTenantFixture.TenantA, $"Paginated Org A-{i}", isPublic: true, portalEnabled: true);
            await _fixture.SeedPublicPartyAsync(MultiTenantFixture.TenantB, $"Paginated Org B-{i}", isPublic: true, portalEnabled: true);
        }

        var service = CreateService();

        var page1 = await service.GetPublicEventsAsync(new PortalFilter(), page: 1, pageSize: 3, ct: default);
        var page2 = await service.GetPublicEventsAsync(new PortalFilter(), page: 2, pageSize: 3, ct: default);

        page1.Items.Should().HaveCount(3);
        page1.TotalCount.Should().Be(10);
        page2.Items.Should().HaveCount(3);

        var page1Ids = page1.Items.Select(e => e.EntityId).ToList();
        var page2Ids = page2.Items.Select(e => e.EntityId).ToList();
        page1Ids.Should().NotIntersectWith(page2Ids);
    }

    [Fact]
    public async Task GetPublicEventsAsync_Should_Return_Empty_When_No_Tenants()
    {
        var service = CreateService();
        var filter = new PortalFilter { TenantId = "nonexistent-tenant" };
        var result = await service.GetPublicEventsAsync(filter, page: 1, pageSize: 50, ct: default);

        result.TotalCount.Should().Be(0);
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPublicEventsAsync_Should_Clamp_PageSize()
    {
        await _fixture.SeedPublicPartyAsync(MultiTenantFixture.TenantA, "Clamp Test", isPublic: true, portalEnabled: true);

        var service = CreateService();

        // pageSize = 0 should be clamped to 1
        var result = await service.GetPublicEventsAsync(new PortalFilter(), page: 1, pageSize: 0, ct: default);
        result.Items.Should().HaveCount(1);
    }

    private FanOutPortalQueryService CreateService()
    {
        return new FanOutPortalQueryService(
            _fixture.CreateTenantQueries(),
            _fixture.CreateTenantConnectionFactory(),
            NullLogger<FanOutPortalQueryService>.Instance);
    }

    private async Task TruncateTenantDataAsync(string tenantId)
    {
        var connStr = BuildTenantConnectionString(tenantId);
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        await conn.ExecuteAsync("TRUNCATE party.parties; DELETE FROM config.settings WHERE key = 'feature.portal.enabled'");
    }

    private string BuildTenantConnectionString(string tenantId)
    {
        var builder = new NpgsqlConnectionStringBuilder(_fixture.SystemConnectionString)
        {
            Database = $"stratum_{tenantId.Replace('-', '_')}",
        };
        return builder.ToString();
    }
}
