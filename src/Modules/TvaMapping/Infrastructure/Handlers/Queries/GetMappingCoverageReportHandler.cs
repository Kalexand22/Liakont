namespace Liakont.Modules.TvaMapping.Infrastructure.Handlers.Queries;

using Liakont.Modules.Ingestion.Contracts.Queries;
using Liakont.Modules.TvaMapping.Contracts.DTOs;
using Liakont.Modules.TvaMapping.Contracts.Queries;
using Liakont.Modules.TvaMapping.Domain.CoverageDetection;
using MediatR;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Common.Infrastructure.DataIsolation;

/// <summary>
/// Handler de la détection proactive des régimes non mappés (item TVA03, F03 §4.3). Confronte les
/// régimes source observés du tenant (base système, schéma <c>ingestion</c> — lus via le contrat du
/// module Ingestion <see cref="ISourceTaxRegimeQueries"/>, accès inter-module autorisé par les
/// Contracts) à la table de mapping du tenant (lue via <see cref="ITvaMappingQueries"/>), puis applique
/// <see cref="MappingCoverageAnalyzer"/>. Le handler ne contient AUCUNE logique fiscale : il résout le
/// tenant, lit, délègue le croisement au domaine, et mappe en DTO.
///
/// Double clé de tenant, issue d'un même principal authentifié donc cohérente (CLAUDE.md n°9/17,
/// INV-008/012) :
/// <list type="bullet">
///   <item><c>company_id</c> (Guid) via <see cref="ICompanyFilter"/> pour la table (base tenant).</item>
///   <item>slug de tenant via <see cref="ITenantContext"/> pour les régimes observés (base système).</item>
/// </list>
/// </summary>
public sealed class GetMappingCoverageReportHandler
    : IRequestHandler<GetMappingCoverageReportQuery, MappingCoverageReportDto>
{
    private readonly ISourceTaxRegimeQueries _sourceTaxRegimeQueries;
    private readonly ITvaMappingQueries _mappingQueries;
    private readonly ICompanyFilter _companyFilter;
    private readonly ITenantContext _tenantContext;

    public GetMappingCoverageReportHandler(
        ISourceTaxRegimeQueries sourceTaxRegimeQueries,
        ITvaMappingQueries mappingQueries,
        ICompanyFilter companyFilter,
        ITenantContext tenantContext)
    {
        _sourceTaxRegimeQueries = sourceTaxRegimeQueries;
        _mappingQueries = mappingQueries;
        _companyFilter = companyFilter;
        _tenantContext = tenantContext;
    }

    public async Task<MappingCoverageReportDto> Handle(
        GetMappingCoverageReportQuery request,
        CancellationToken cancellationToken)
    {
        var companyId = _companyFilter.GetRequiredCompanyId();

        var tenantSlug = _tenantContext.TenantId;
        if (string.IsNullOrWhiteSpace(tenantSlug))
        {
            throw new InvalidOperationException(
                "Impossible de calculer la couverture du mapping TVA : aucun tenant résolu pour la requête. "
                + "Action : vérifier que le compte connecté est rattaché à un tenant (slug) actif.");
        }

        var observedDtos = await _sourceTaxRegimeQueries.ListByTenantAsync(tenantSlug, cancellationToken);
        var observed = new List<ObservedSourceRegime>(observedDtos.Count);
        foreach (var dto in observedDtos)
        {
            observed.Add(new ObservedSourceRegime
            {
                Code = dto.Code,
                Label = dto.Label,
                Occurrences = dto.Occurrences,
                LastSeenAtUtc = dto.LastSeenAtUtc,
            });
        }

        var tableDto = await _mappingQueries.GetMappingTable(companyId, cancellationToken);
        MappingTableSummary? summary = tableDto is null
            ? null
            : new MappingTableSummary
            {
                MappingVersion = tableDto.MappingVersion,
                IsValidated = tableDto.IsValidated,
                MappedRegimeCodes = tableDto.Rules.Select(rule => rule.SourceRegimeCode).ToArray(),
            };

        var report = MappingCoverageAnalyzer.Analyze(observed, summary);
        return ToDto(report);
    }

    private static MappingCoverageReportDto ToDto(MappingCoverageReport report)
    {
        return new MappingCoverageReportDto
        {
            IsTableConfigured = report.IsTableConfigured,
            MappingVersion = report.MappingVersion,
            IsTableValidated = report.IsTableValidated,
            Verdict = report.Verdict.ToString(),
            CoveredRegimes = report.CoveredRegimes.Select(ToDto).ToArray(),
            AbsentRegimes = report.AbsentRegimes.Select(ToDto).ToArray(),
        };
    }

    private static RegimeCoverageDto ToDto(ObservedSourceRegime regime)
    {
        return new RegimeCoverageDto
        {
            Code = regime.Code,
            Label = regime.Label,
            Occurrences = regime.Occurrences,
            LastSeenAtUtc = regime.LastSeenAtUtc,
        };
    }
}
