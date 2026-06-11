namespace Liakont.Modules.TvaMapping.Infrastructure.Handlers.Commands;

using Liakont.Modules.TvaMapping.Application;
using Liakont.Modules.TvaMapping.Contracts.Commands;
using Liakont.Modules.TvaMapping.Infrastructure.Seed;
using MediatR;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.Infrastructure.DataIsolation;

/// <summary>
/// Importe la table de mapping TVA d'un fichier de seed dans le tenant courant (item FIX01b, chemin
/// OPS03). IDEMPOTENT : si une table est déjà paramétrée, l'import est ignoré (jamais d'écrasement d'une
/// table éditée par l'opérateur). La lecture du seed (<see cref="MappingTableSeedReader"/>) ne devine
/// AUCUNE règle fiscale (CLAUDE.md n°2) et conserve le marqueur « table d'exemple » + l'état NON VALIDÉE
/// du seed. Aucune clé ni secret n'est concerné (table de paramétrage public).
/// </summary>
public sealed class ImportMappingTableSeedHandler : IRequestHandler<ImportMappingTableSeedCommand, bool>
{
    private readonly ITvaMappingUnitOfWorkFactory _uowFactory;
    private readonly ICompanyFilter _companyFilter;
    private readonly IActorContextAccessor _actorContextAccessor;

    public ImportMappingTableSeedHandler(
        ITvaMappingUnitOfWorkFactory uowFactory,
        ICompanyFilter companyFilter,
        IActorContextAccessor actorContextAccessor)
    {
        _uowFactory = uowFactory;
        _companyFilter = companyFilter;
        _actorContextAccessor = actorContextAccessor;
    }

    public async Task<bool> Handle(ImportMappingTableSeedCommand request, CancellationToken cancellationToken)
    {
        var companyId = ResolveCompanyId(request);

        await using var uow = await _uowFactory.BeginAsync(cancellationToken);

        // Idempotent : ne JAMAIS écraser une table déjà paramétrée (l'opérateur peut l'avoir éditée /
        // validée). L'import de seed ne sert qu'à AMORCER un tenant vierge.
        var existing = await uow.GetForUpdateAsync(companyId, cancellationToken);
        if (existing is not null)
        {
            return false;
        }

        // Lecture pure du fichier de seed (le marqueur « table d'exemple » et l'état NON VALIDÉE sont
        // conservés ; tout code inconnu est rejeté par le reader avant l'insertion).
        var table = await MappingTableSeedReader.ImportFileAsync(request.SeedFilePath, companyId, cancellationToken);

        try
        {
            await uow.InsertMappingTableAsync(table, cancellationToken);
            await uow.CommitAsync(cancellationToken);
        }
        catch (ConflictException)
        {
            // Course concurrente (un autre OPS03 / « Créer la table » console a inséré entre-temps) :
            // l'import de seed n'écrase jamais — idempotent, on ignore (CLAUDE.md : amorçage seulement).
            return false;
        }

        return true;
    }

    /// <summary>
    /// Résout la clé de scoping de l'import. Un <see cref="ImportMappingTableSeedCommand.CompanyId"/>
    /// explicite n'est honoré que sur un chemin PRIVILÉGIÉ (amorçage de démarrage, provisioning OPS03 scopé
    /// sur le tenant cible) — ces chemins n'ont pas d'acteur de tenant (companyId du contexte courant
    /// <c>null</c>), donc le filtre de société ambiant ne peut rien résoudre (cause du seed partiel FIX203a).
    /// Si un acteur de tenant est présent (chemin opérateur) et qu'un companyId explicite CONTREDIT sa
    /// société, l'import est REFUSÉ (garde anti-injection cross-tenant, CLAUDE.md n°9/17). Sans companyId
    /// explicite, on retombe sur la société du contexte courant (<see cref="ICompanyFilter"/>). Même
    /// pattern que <c>ImportTenantSeedHandler.ResolveCompanyId</c> (FIX01a).
    /// </summary>
    private Guid ResolveCompanyId(ImportMappingTableSeedCommand request)
    {
        if (request.CompanyId is not { } explicitCompanyId)
        {
            return _companyFilter.GetRequiredCompanyId();
        }

        var actorCompanyId = _actorContextAccessor.Current.CompanyId;
        if (actorCompanyId is { } actorId && actorId != explicitCompanyId)
        {
            throw new ConflictException(
                "Le companyId explicite de l'import de table de mapping ne peut pas différer de la société du "
                + "contexte courant (garde anti-injection cross-tenant). L'override n'est destiné qu'aux chemins "
                + "de provisioning sans acteur de tenant.");
        }

        return explicitCompanyId;
    }
}
