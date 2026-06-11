namespace Liakont.Modules.TvaMapping.Infrastructure.Handlers.Commands;

using Liakont.Modules.TvaMapping.Application;
using Liakont.Modules.TvaMapping.Contracts.Commands;
using Liakont.Modules.TvaMapping.Domain.Entities;
using Liakont.Modules.TvaMapping.Domain.Services;
using MediatR;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.Infrastructure.DataIsolation;

/// <summary>
/// Ajoute une règle à la table de mapping TVA du tenant courant (item TVA05 §1). La règle est validée
/// structurellement (doublon, E à 0 % → VATEX, taux). La mutation efface l'état de validation
/// (item TVA05 §2) et journalise l'opération (append-only) de façon ATOMIQUE avec l'écriture (§5).
/// Si AUCUNE table n'est encore paramétrée, elle est CRÉÉE implicitement (item FIX01b) — table vide
/// « NON VALIDÉE », comportement <c>block</c> — et la création est journalisée (<c>CreateTable</c>)
/// dans la même transaction, plutôt que de rejeter l'opération (plus de <c>NotFoundException</c>).
/// </summary>
public sealed class AddMappingRuleHandler : IRequestHandler<AddMappingRuleCommand>
{
    private readonly ITvaMappingUnitOfWorkFactory _uowFactory;
    private readonly ICompanyFilter _companyFilter;
    private readonly IActorContextAccessor _actorContextAccessor;

    public AddMappingRuleHandler(
        ITvaMappingUnitOfWorkFactory uowFactory,
        ICompanyFilter companyFilter,
        IActorContextAccessor actorContextAccessor)
    {
        _uowFactory = uowFactory;
        _companyFilter = companyFilter;
        _actorContextAccessor = actorContextAccessor;
    }

    public async Task Handle(AddMappingRuleCommand request, CancellationToken cancellationToken)
    {
        var companyId = _companyFilter.GetRequiredCompanyId();
        var actor = _actorContextAccessor.Current;

        var rule = MappingRuleFactory.Create(
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

        var table = await uow.GetForUpdateAsync(companyId, cancellationToken);

        if (table is null)
        {
            // Création implicite (item FIX01b) : la première règle d'un tenant sans table crée la table
            // (vide, « NON VALIDÉE », block) puis y ajoute la règle. Naissance + ajout sont journalisés
            // dans la MÊME transaction (CreateTable puis AddRule) — append-only, atomique avec l'insertion.
            table = MappingTable.Create(
                companyId,
                MappingTable.InitialMappingVersion,
                validatedBy: null,
                validatedDate: null,
                MappingDefaultBehavior.Block,
                rules: []);

            // CreateTable journalise la NAISSANCE de la table vide (RuleCount=0, cohérent avec CreateMappingTableHandler) ;
            // l'entrée est figée AVANT l'ajout de la règle.
            var createEntry = MappingChangeLogFactory.ForCreateTable(table, actor.UserId, actor.DisplayName);

            table.AddRule(rule);
            var addEntry = MappingChangeLogFactory.ForAddRule(table, rule, actor.UserId, actor.DisplayName);
            await uow.InsertMappingTableAsync(table, [createEntry, addEntry], cancellationToken);
            await uow.CommitAsync(cancellationToken);
            return;
        }

        table.AddRule(rule);

        var entry = MappingChangeLogFactory.ForAddRule(table, rule, actor.UserId, actor.DisplayName);
        await uow.SaveMutationAsync(table, entry, cancellationToken);
        await uow.CommitAsync(cancellationToken);
    }
}
