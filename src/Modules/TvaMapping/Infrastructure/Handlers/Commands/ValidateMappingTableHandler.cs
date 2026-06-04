namespace Liakont.Modules.TvaMapping.Infrastructure.Handlers.Commands;

using Liakont.Modules.TvaMapping.Application;
using Liakont.Modules.TvaMapping.Contracts.Commands;
using MediatR;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.Infrastructure.DataIsolation;

/// <summary>
/// Valide humainement la table de mapping TVA du tenant courant (workflow expert-comptable,
/// item TVA05 §4) : renseigne <c>validatedBy</c> et la date courante, ce qui lève la suspension des
/// envois en production (garde-fou PIP01). La validation est journalisée (append-only) de façon atomique
/// avec l'écriture (item TVA05 §5).
/// </summary>
public sealed class ValidateMappingTableHandler : IRequestHandler<ValidateMappingTableCommand>
{
    private readonly ITvaMappingUnitOfWorkFactory _uowFactory;
    private readonly ICompanyFilter _companyFilter;
    private readonly IActorContextAccessor _actorContextAccessor;

    public ValidateMappingTableHandler(
        ITvaMappingUnitOfWorkFactory uowFactory,
        ICompanyFilter companyFilter,
        IActorContextAccessor actorContextAccessor)
    {
        _uowFactory = uowFactory;
        _companyFilter = companyFilter;
        _actorContextAccessor = actorContextAccessor;
    }

    public async Task Handle(ValidateMappingTableCommand request, CancellationToken cancellationToken)
    {
        var companyId = _companyFilter.GetRequiredCompanyId();
        var actor = _actorContextAccessor.Current;

        await using var uow = await _uowFactory.BeginAsync(cancellationToken);

        var table = await uow.GetForUpdateAsync(companyId, cancellationToken)
            ?? throw new NotFoundException(MappingEditMessages.NoTableForTenant);

        var previousValidatedBy = table.ValidatedBy;
        var previousValidatedDate = table.ValidatedDate;

        table.Validate(request.ValidatedBy);

        var entry = MappingChangeLogFactory.ForValidate(
            table, previousValidatedBy, previousValidatedDate, actor.UserId, actor.DisplayName);
        await uow.SaveMutationAsync(table, entry, cancellationToken);
        await uow.CommitAsync(cancellationToken);
    }
}
