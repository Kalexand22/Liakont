namespace Liakont.Modules.TvaMapping.Infrastructure.Handlers.Commands;

using Liakont.Modules.TvaMapping.Application;
using Liakont.Modules.TvaMapping.Contracts.Commands;
using Liakont.Modules.TvaMapping.Domain.Services;
using MediatR;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.Infrastructure.DataIsolation;

/// <summary>
/// Remplace les valeurs d'une règle existante (item TVA05 §1), identifiée par (code régime, part). Lève
/// <see cref="NotFoundException"/> si la règle n'existe pas. La mutation efface l'état de validation
/// (item TVA05 §2) et journalise la valeur avant/après (append-only) de façon atomique (§5).
/// </summary>
public sealed class UpdateMappingRuleHandler : IRequestHandler<UpdateMappingRuleCommand>
{
    private readonly ITvaMappingUnitOfWorkFactory _uowFactory;
    private readonly ICompanyFilter _companyFilter;
    private readonly IActorContextAccessor _actorContextAccessor;

    public UpdateMappingRuleHandler(
        ITvaMappingUnitOfWorkFactory uowFactory,
        ICompanyFilter companyFilter,
        IActorContextAccessor actorContextAccessor)
    {
        _uowFactory = uowFactory;
        _companyFilter = companyFilter;
        _actorContextAccessor = actorContextAccessor;
    }

    public async Task Handle(UpdateMappingRuleCommand request, CancellationToken cancellationToken)
    {
        var companyId = _companyFilter.GetRequiredCompanyId();
        var actor = _actorContextAccessor.Current;

        var targetPart = MappingRuleFactory.ParsePart(request.Part);
        var replacement = MappingRuleFactory.Create(
            request.SourceRegimeCode,
            request.Label,
            request.Part,
            request.SourceFlags,
            request.Category,
            request.Vatex,
            request.Note,
            request.RateMode,
            request.RateValue);

        await using var uow = await _uowFactory.BeginAsync(cancellationToken);

        var table = await uow.GetForUpdateAsync(companyId, cancellationToken)
            ?? throw new NotFoundException(MappingEditMessages.NoTableForTenant);

        var previous = table.UpdateRule(request.SourceRegimeCode, targetPart, replacement);

        var entry = MappingChangeLogFactory.ForUpdateRule(table, previous, replacement, actor.UserId, actor.DisplayName);
        await uow.SaveMutationAsync(table, entry, cancellationToken);
        await uow.CommitAsync(cancellationToken);
    }
}
