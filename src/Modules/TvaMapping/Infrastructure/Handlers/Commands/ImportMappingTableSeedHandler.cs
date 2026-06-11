namespace Liakont.Modules.TvaMapping.Infrastructure.Handlers.Commands;

using Liakont.Modules.TvaMapping.Application;
using Liakont.Modules.TvaMapping.Contracts.Commands;
using Liakont.Modules.TvaMapping.Infrastructure.Seed;
using MediatR;
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

    public ImportMappingTableSeedHandler(
        ITvaMappingUnitOfWorkFactory uowFactory,
        ICompanyFilter companyFilter)
    {
        _uowFactory = uowFactory;
        _companyFilter = companyFilter;
    }

    public async Task<bool> Handle(ImportMappingTableSeedCommand request, CancellationToken cancellationToken)
    {
        var companyId = _companyFilter.GetRequiredCompanyId();

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

        await uow.InsertMappingTableAsync(table, cancellationToken);
        await uow.CommitAsync(cancellationToken);
        return true;
    }
}
