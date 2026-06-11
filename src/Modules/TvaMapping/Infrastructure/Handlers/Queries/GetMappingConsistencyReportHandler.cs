namespace Liakont.Modules.TvaMapping.Infrastructure.Handlers.Queries;

using Liakont.Modules.Ingestion.Contracts.Queries;
using Liakont.Modules.TvaMapping.Contracts.DTOs;
using Liakont.Modules.TvaMapping.Contracts.Queries;
using Liakont.Modules.TvaMapping.Domain.ConsistencyDetection;
using Liakont.Modules.TvaMapping.Domain.Entities;
using MediatR;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Common.Infrastructure.DataIsolation;

/// <summary>
/// Handler du contrôle de cohérence du paramétrage de mapping TVA (lot FIX03), symétrique de
/// <see cref="GetMappingCoverageReportHandler"/>. Confronte les règles de la table du tenant (lue via
/// <see cref="ITvaMappingQueries"/>, base tenant) aux régimes source observés (lus via
/// <see cref="ISourceTaxRegimeQueries"/>, base système — accès inter-module autorisé par les Contracts)
/// et aux parts consultées (dérivées de l'activation FOURNIE par l'appelant), puis délègue le croisement
/// à <see cref="MappingConsistencyAnalyzer"/>. Le handler ne contient AUCUNE logique fiscale.
///
/// Double clé de tenant, issue d'un même principal authentifié donc cohérente (CLAUDE.md n°9/17,
/// INV-008/012) : <c>company_id</c> via <see cref="ICompanyFilter"/> pour la table, slug via
/// <see cref="ITenantContext"/> pour les régimes observés.
/// </summary>
public sealed class GetMappingConsistencyReportHandler
    : IRequestHandler<GetMappingConsistencyReportQuery, MappingConsistencyReportDto>
{
    private readonly ISourceTaxRegimeQueries _sourceTaxRegimeQueries;
    private readonly ITvaMappingQueries _mappingQueries;
    private readonly ICompanyFilter _companyFilter;
    private readonly ITenantContext _tenantContext;

    public GetMappingConsistencyReportHandler(
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

    public async Task<MappingConsistencyReportDto> Handle(
        GetMappingConsistencyReportQuery request,
        CancellationToken cancellationToken)
    {
        var companyId = _companyFilter.GetRequiredCompanyId();

        var tenantSlug = _tenantContext.TenantId;
        if (string.IsNullOrWhiteSpace(tenantSlug))
        {
            throw new InvalidOperationException(
                "Impossible de calculer la cohérence du mapping TVA : aucun tenant résolu pour la requête. "
                + "Action : vérifier que le compte connecté est rattaché à un tenant (slug) actif.");
        }

        var tableDto = await _mappingQueries.GetMappingTable(companyId, cancellationToken);
        if (tableDto is null)
        {
            return new MappingConsistencyReportDto
            {
                IsTableConfigured = false,
                DeadRules = Array.Empty<DeadMappingRuleDto>(),
            };
        }

        var observedDtos = await _sourceTaxRegimeQueries.ListByTenantAsync(tenantSlug, cancellationToken);
        var observedCodes = observedDtos.Select(dto => dto.Code).ToArray();

        var rules = tableDto.Rules
            .Select(rule => new MappingRuleConsistencyView
            {
                SourceRegimeCode = rule.SourceRegimeCode,
                Part = Enum.Parse<MappingPart>(rule.Part),
                Label = rule.Label,
            })
            .ToArray();

        var consultedParts = ConsultedMappingParts.For(request.AuctionVerticalEnabled);

        var report = MappingConsistencyAnalyzer.Analyze(rules, consultedParts, observedCodes, tableConfigured: true);
        return ToDto(report);
    }

    private static MappingConsistencyReportDto ToDto(MappingConsistencyReport report)
    {
        return new MappingConsistencyReportDto
        {
            IsTableConfigured = report.IsTableConfigured,
            DeadRules = report.DeadRules.Select(ToDto).ToArray(),
        };
    }

    private static DeadMappingRuleDto ToDto(DeadMappingRule rule)
    {
        return new DeadMappingRuleDto
        {
            SourceRegimeCode = rule.SourceRegimeCode,
            Part = rule.Part.ToString(),
            Label = rule.Label,
            Reasons = rule.Reasons.Select(reason => reason.ToString()).ToArray(),
        };
    }
}
