namespace Liakont.Modules.TvaMapping.Infrastructure.Handlers.Commands;

using Liakont.Modules.TvaMapping.Application;
using Liakont.Modules.TvaMapping.Contracts.Commands;
using Liakont.Modules.TvaMapping.Domain.Services;
using MediatR;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.Infrastructure.DataIsolation;

/// <summary>
/// Supprime une règle existante (item TVA05 §1), identifiée par (code régime, part). Lève
/// <see cref="NotFoundException"/> si la règle n'existe pas. La suppression efface l'état de validation
/// (item TVA05 §2) et journalise la valeur supprimée (append-only) de façon atomique (§5).
/// </summary>
public sealed class RemoveMappingRuleHandler : IRequestHandler<RemoveMappingRuleCommand>
{
    private readonly ITvaMappingUnitOfWorkFactory _uowFactory;
    private readonly ICompanyFilter _companyFilter;
    private readonly IActorContextAccessor _actorContextAccessor;

    public RemoveMappingRuleHandler(
        ITvaMappingUnitOfWorkFactory uowFactory,
        ICompanyFilter companyFilter,
        IActorContextAccessor actorContextAccessor)
    {
        _uowFactory = uowFactory;
        _companyFilter = companyFilter;
        _actorContextAccessor = actorContextAccessor;
    }

    public async Task Handle(RemoveMappingRuleCommand request, CancellationToken cancellationToken)
    {
        var companyId = _companyFilter.GetRequiredCompanyId();
        var actor = _actorContextAccessor.Current;

        var targetPart = MappingRuleFactory.ParsePart(request.Part);

        await using var uow = await _uowFactory.BeginAsync(cancellationToken);

        var table = await uow.GetForUpdateAsync(companyId, cancellationToken)
            ?? throw new NotFoundException(MappingEditMessages.NoTableForTenant);

        var removed = table.RemoveRule(request.SourceRegimeCode, targetPart);

        var entry = MappingChangeLogFactory.ForRemoveRule(table, removed, actor.UserId, actor.DisplayName);
        await uow.SaveMutationAsync(table, entry, cancellationToken);
        await uow.CommitAsync(cancellationToken);
    }
}
