namespace Liakont.Modules.TvaMapping.Infrastructure.Handlers.Commands;

using Liakont.Modules.TvaMapping.Application;
using Liakont.Modules.TvaMapping.Contracts.Commands;
using Liakont.Modules.TvaMapping.Domain.Entities;
using MediatR;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.Infrastructure.DataIsolation;

/// <summary>
/// Crée une table de mapping TVA VIDE et « NON VALIDÉE » pour le tenant courant (item FIX01b, chemin
/// bouton « Créer la table »). La table naît avec la version initiale, le comportement par défaut
/// <c>block</c> et aucune règle ; la création est journalisée (append-only, <c>CreateTable</c>) de façon
/// ATOMIQUE avec l'insertion. Lève une <c>ConflictException</c> si une table existe déjà (jamais
/// d'écrasement silencieux — CLAUDE.md n°3).
/// </summary>
public sealed class CreateMappingTableHandler : IRequestHandler<CreateMappingTableCommand>
{
    private readonly ITvaMappingUnitOfWorkFactory _uowFactory;
    private readonly ICompanyFilter _companyFilter;
    private readonly IActorContextAccessor _actorContextAccessor;

    public CreateMappingTableHandler(
        ITvaMappingUnitOfWorkFactory uowFactory,
        ICompanyFilter companyFilter,
        IActorContextAccessor actorContextAccessor)
    {
        _uowFactory = uowFactory;
        _companyFilter = companyFilter;
        _actorContextAccessor = actorContextAccessor;
    }

    public async Task Handle(CreateMappingTableCommand request, CancellationToken cancellationToken)
    {
        var companyId = _companyFilter.GetRequiredCompanyId();
        var actor = _actorContextAccessor.Current;

        var table = MappingTable.Create(
            companyId,
            MappingTable.InitialMappingVersion,
            validatedBy: null,
            validatedDate: null,
            MappingDefaultBehavior.Block,
            rules: []);

        var creationEntry = MappingChangeLogFactory.ForCreateTable(table, actor.UserId, actor.DisplayName);

        await using var uow = await _uowFactory.BeginAsync(cancellationToken);
        await uow.InsertMappingTableAsync(table, [creationEntry], cancellationToken);
        await uow.CommitAsync(cancellationToken);
    }
}
