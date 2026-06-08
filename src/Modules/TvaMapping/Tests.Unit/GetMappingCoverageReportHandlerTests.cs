namespace Liakont.Modules.TvaMapping.Tests.Unit;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Ingestion.Contracts.DTOs;
using Liakont.Modules.Ingestion.Contracts.Queries;
using Liakont.Modules.TvaMapping.Contracts.DTOs;
using Liakont.Modules.TvaMapping.Contracts.Queries;
using Liakont.Modules.TvaMapping.Infrastructure.Handlers.Queries;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Common.Infrastructure.DataIsolation;
using Xunit;

/// <summary>
/// Handler de détection de couverture (item TVA03) : vérifie le routage tenant CORRECT (slug pour les
/// régimes observés en base système, <c>company_id</c> pour la table en base tenant — INV-008/012,
/// CLAUDE.md n°9/17), la délégation du croisement au domaine et le mapping en DTO. Le handler ne porte
/// aucune logique fiscale (déléguée à <c>MappingCoverageAnalyzer</c>, testé séparément).
/// </summary>
public sealed class GetMappingCoverageReportHandlerTests
{
    private static readonly DateTimeOffset SeenAt = new(2026, 6, 4, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_RoutesResolvedSlugToRegimeQuery_AndCompanyIdToMappingQuery()
    {
        var companyId = Guid.NewGuid();
        var regimeQueries = new FakeSourceTaxRegimeQueries(new[] { Regime("REGIME-A") });
        var mappingQueries = new FakeTvaMappingQueries(TableDto(validated: true, Rule("REGIME-A")));
        var handler = CreateHandler(regimeQueries, mappingQueries, companyId, tenantSlug: "acme");

        await handler.Handle(new GetMappingCoverageReportQuery(), CancellationToken.None);

        regimeQueries.CapturedTenantId.Should().Be("acme");
        mappingQueries.CapturedCompanyId.Should().Be(companyId);
    }

    [Fact]
    public async Task Handle_AllRegimesMapped_ReturnsCompleteDto()
    {
        var regimeQueries = new FakeSourceTaxRegimeQueries(new[] { Regime("REGIME-A"), Regime("REGIME-B") });
        var mappingQueries = new FakeTvaMappingQueries(TableDto(validated: true, Rule("REGIME-A"), Rule("REGIME-B")));
        var handler = CreateHandler(regimeQueries, mappingQueries);

        var dto = await handler.Handle(new GetMappingCoverageReportQuery(), CancellationToken.None);

        dto.Verdict.Should().Be("Complete");
        dto.IsTableConfigured.Should().BeTrue();
        dto.IsTableValidated.Should().BeTrue();
        dto.MappingVersion.Should().Be("tenant-v1");
        dto.AbsentRegimes.Should().BeEmpty();
        dto.CoveredRegimes.Select(r => r.Code).Should().Equal("REGIME-A", "REGIME-B");
    }

    [Fact]
    public async Task Handle_SomeRegimesUnmapped_ReturnsIncompleteDto_WithAbsentList()
    {
        var regimeQueries = new FakeSourceTaxRegimeQueries(new[]
        {
            Regime("REGIME-A", occ: 5),
            Regime("REGIME-X", occ: 3, label: "Inconnu"),
        });
        var mappingQueries = new FakeTvaMappingQueries(TableDto(validated: false, Rule("REGIME-A")));
        var handler = CreateHandler(regimeQueries, mappingQueries);

        var dto = await handler.Handle(new GetMappingCoverageReportQuery(), CancellationToken.None);

        dto.Verdict.Should().Be("Incomplete");
        dto.IsTableValidated.Should().BeFalse();
        dto.AbsentRegimes.Should().ContainSingle();
        var absent = dto.AbsentRegimes.Single();
        absent.Code.Should().Be("REGIME-X");
        absent.Label.Should().Be("Inconnu");
        absent.Occurrences.Should().Be(3);
        absent.LastSeenAtUtc.Should().Be(SeenAt);
    }

    [Fact]
    public async Task Handle_NoTableConfigured_ReturnsIncompleteNotConfigured()
    {
        var regimeQueries = new FakeSourceTaxRegimeQueries(new[] { Regime("REGIME-A") });
        var mappingQueries = new FakeTvaMappingQueries(table: null);
        var handler = CreateHandler(regimeQueries, mappingQueries);

        var dto = await handler.Handle(new GetMappingCoverageReportQuery(), CancellationToken.None);

        dto.Verdict.Should().Be("Incomplete");
        dto.IsTableConfigured.Should().BeFalse();
        dto.IsTableValidated.Should().BeFalse();
        dto.MappingVersion.Should().BeNull();
        dto.AbsentRegimes.Select(r => r.Code).Should().Equal("REGIME-A");
    }

    [Fact]
    public async Task Handle_NoObservedRegimes_ReturnsComplete()
    {
        var regimeQueries = new FakeSourceTaxRegimeQueries(Array.Empty<SourceTaxRegimeSummaryDto>());
        var mappingQueries = new FakeTvaMappingQueries(TableDto(validated: true, Rule("REGIME-A")));
        var handler = CreateHandler(regimeQueries, mappingQueries);

        var dto = await handler.Handle(new GetMappingCoverageReportQuery(), CancellationToken.None);

        dto.Verdict.Should().Be("Complete");
        dto.CoveredRegimes.Should().BeEmpty();
        dto.AbsentRegimes.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handle_UnresolvedTenant_Throws(string? slug)
    {
        var regimeQueries = new FakeSourceTaxRegimeQueries(Array.Empty<SourceTaxRegimeSummaryDto>());
        var mappingQueries = new FakeTvaMappingQueries(table: null);
        var handler = CreateHandler(regimeQueries, mappingQueries, tenantSlug: slug);

        var act = () => handler.Handle(new GetMappingCoverageReportQuery(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        regimeQueries.CapturedTenantId.Should().BeNull("aucune lecture ne doit partir sans tenant résolu");
    }

    private static GetMappingCoverageReportHandler CreateHandler(
        ISourceTaxRegimeQueries regimeQueries,
        ITvaMappingQueries mappingQueries,
        Guid? companyId = null,
        string? tenantSlug = "acme")
    {
        return new GetMappingCoverageReportHandler(
            regimeQueries,
            mappingQueries,
            new StubCompanyFilter(companyId ?? Guid.NewGuid()),
            new StubTenantContext(tenantSlug));
    }

    private static SourceTaxRegimeSummaryDto Regime(string code, long occ = 1, string? label = null) => new()
    {
        Code = code,
        Label = label,
        Occurrences = occ,
        LastSeenAtUtc = SeenAt,
    };

    private static MappingRuleDto Rule(string code, string part = "Adjudication") => new()
    {
        SourceRegimeCode = code,
        Part = part,
        Category = "S",
        RateMode = "Fixed",
        RateValue = 20m,
    };

    private static MappingTableDto TableDto(bool validated, params MappingRuleDto[] rules) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = Guid.NewGuid(),
        MappingVersion = "tenant-v1",
        ValidatedBy = validated ? "Expert-comptable" : null,
        ValidatedDate = validated ? new DateOnly(2026, 7, 15) : null,
        IsValidated = validated,
        DefaultBehavior = "Block",
        Rules = rules,
        CreatedAt = SeenAt,
        UpdatedAt = null,
    };

    private sealed class FakeSourceTaxRegimeQueries : ISourceTaxRegimeQueries
    {
        private readonly IReadOnlyList<SourceTaxRegimeSummaryDto> _result;

        public FakeSourceTaxRegimeQueries(IReadOnlyList<SourceTaxRegimeSummaryDto> result)
        {
            _result = result;
        }

        public string? CapturedTenantId { get; private set; }

        public Task<IReadOnlyList<SourceTaxRegimeSummaryDto>> ListByTenantAsync(
            string tenantId,
            CancellationToken cancellationToken = default)
        {
            CapturedTenantId = tenantId;
            return Task.FromResult(_result);
        }
    }

    private sealed class FakeTvaMappingQueries : ITvaMappingQueries
    {
        private readonly MappingTableDto? _table;

        public FakeTvaMappingQueries(MappingTableDto? table)
        {
            _table = table;
        }

        public Guid CapturedCompanyId { get; private set; }

        public Task<MappingTableDto?> GetMappingTable(Guid companyId, CancellationToken ct = default)
        {
            CapturedCompanyId = companyId;
            return Task.FromResult(_table);
        }

        public Task<IReadOnlyList<MappingChangeLogEntryDto>> GetChangeLog(Guid companyId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<MappingChangeLogEntryDto>>(Array.Empty<MappingChangeLogEntryDto>());
    }

    private sealed class StubCompanyFilter : ICompanyFilter
    {
        private readonly Guid _companyId;

        public StubCompanyFilter(Guid companyId)
        {
            _companyId = companyId;
        }

        public Guid GetRequiredCompanyId() => _companyId;
    }

    private sealed class StubTenantContext : ITenantContext
    {
        public StubTenantContext(string? tenantId)
        {
            TenantId = tenantId;
        }

        public string? TenantId { get; }

        public bool IsResolved => !string.IsNullOrWhiteSpace(TenantId);
    }
}
