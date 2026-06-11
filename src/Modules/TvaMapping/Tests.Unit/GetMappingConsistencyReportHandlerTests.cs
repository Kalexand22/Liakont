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
/// Handler du contrôle de cohérence (lot FIX03) : vérifie le routage tenant (slug pour les régimes
/// observés, <c>company_id</c> pour la table — INV-008/012), la dérivation des parts consultées depuis
/// l'activation FOURNIE par l'appelant, et le mapping en DTO. Le handler ne porte aucune logique fiscale
/// (déléguée à <c>MappingConsistencyAnalyzer</c>, testé séparément).
/// </summary>
public sealed class GetMappingConsistencyReportHandlerTests
{
    private static readonly DateTimeOffset SeenAt = new(2026, 6, 4, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_RoutesResolvedSlugToRegimeQuery_AndCompanyIdToMappingQuery()
    {
        var companyId = Guid.NewGuid();
        var regimeQueries = new FakeSourceTaxRegimeQueries(new[] { Regime("R-A") });
        var mappingQueries = new FakeTvaMappingQueries(TableDto(Rule("R-A", "Autre")));
        var handler = CreateHandler(regimeQueries, mappingQueries, companyId, tenantSlug: "acme");

        await handler.Handle(new GetMappingConsistencyReportQuery(), CancellationToken.None);

        regimeQueries.CapturedTenantId.Should().Be("acme");
        mappingQueries.CapturedCompanyId.Should().Be(companyId);
    }

    [Fact]
    public async Task Handle_AdjudicationRule_IsReportedDead_PipelineConsultsAutreOnly()
    {
        // Le pipeline ne consulte que la part Autre (PIP03b gelé) : une règle Adjudication est morte,
        // indépendamment de toute activation du vertical (qui ne gouverne que l'éditeur).
        var regimeQueries = new FakeSourceTaxRegimeQueries(new[] { Regime("R-A") });
        var mappingQueries = new FakeTvaMappingQueries(TableDto(Rule("R-A", "Adjudication"), Rule("R-A", "Autre")));
        var handler = CreateHandler(regimeQueries, mappingQueries);

        var dto = await handler.Handle(new GetMappingConsistencyReportQuery(), CancellationToken.None);

        dto.IsTableConfigured.Should().BeTrue();
        dto.DeadRules.Should().ContainSingle();
        var dead = dto.DeadRules.Single();
        dead.SourceRegimeCode.Should().Be("R-A");
        dead.Part.Should().Be("Adjudication");
        dead.Reasons.Should().Equal("PartNotConsulted");
    }

    [Fact]
    public async Task Handle_AutreRuleOnObservedRegime_IsNotDead()
    {
        var regimeQueries = new FakeSourceTaxRegimeQueries(new[] { Regime("R-A") });
        var mappingQueries = new FakeTvaMappingQueries(TableDto(Rule("R-A", "Autre")));
        var handler = CreateHandler(regimeQueries, mappingQueries);

        var dto = await handler.Handle(new GetMappingConsistencyReportQuery(), CancellationToken.None);

        dto.DeadRules.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_NoTableConfigured_ReturnsNotConfigured_NoDeadRules()
    {
        var regimeQueries = new FakeSourceTaxRegimeQueries(new[] { Regime("R-A") });
        var mappingQueries = new FakeTvaMappingQueries(table: null);
        var handler = CreateHandler(regimeQueries, mappingQueries);

        var dto = await handler.Handle(new GetMappingConsistencyReportQuery(), CancellationToken.None);

        dto.IsTableConfigured.Should().BeFalse();
        dto.DeadRules.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handle_UnresolvedTenant_Throws_AndReadsNothing(string? slug)
    {
        var regimeQueries = new FakeSourceTaxRegimeQueries(Array.Empty<SourceTaxRegimeSummaryDto>());
        var mappingQueries = new FakeTvaMappingQueries(TableDto(Rule("R-A", "Autre")));
        var handler = CreateHandler(regimeQueries, mappingQueries, tenantSlug: slug);

        var act = () => handler.Handle(new GetMappingConsistencyReportQuery(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        regimeQueries.CapturedTenantId.Should().BeNull("aucune lecture ne doit partir sans tenant résolu");
    }

    private static GetMappingConsistencyReportHandler CreateHandler(
        ISourceTaxRegimeQueries regimeQueries,
        ITvaMappingQueries mappingQueries,
        Guid? companyId = null,
        string? tenantSlug = "acme")
    {
        return new GetMappingConsistencyReportHandler(
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

    private static MappingRuleDto Rule(string code, string part) => new()
    {
        SourceRegimeCode = code,
        Part = part,
        Category = "S",
        RateMode = "Fixed",
        RateValue = 20m,
    };

    private static MappingTableDto TableDto(params MappingRuleDto[] rules) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = Guid.NewGuid(),
        MappingVersion = "tenant-v1",
        ValidatedBy = null,
        ValidatedDate = null,
        IsValidated = false,
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
