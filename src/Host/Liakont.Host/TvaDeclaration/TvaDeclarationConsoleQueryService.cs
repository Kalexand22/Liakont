namespace Liakont.Host.TvaDeclaration;

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Pipeline.Contracts.Queries;

/// <summary>
/// Implémentation de <see cref="ITvaDeclarationConsoleQueries"/> : lit le registre de la marge agrégé par mois ×
/// devise × taux (<see cref="IMarginRegistryQueries"/>, L2) et projette en <see cref="TvaDeclarationViewModel"/>.
/// Lecture tenant-scopée par construction (base du tenant courant — CLAUDE.md n°9/17). AUCUNE règle fiscale n'est
/// dérivée : la base HT et la TVA sur marge viennent du registre (peuplé au CHECK depuis les cœurs purs sourcés),
/// reportées telles quelles (CLAUDE.md n°2) ; les totaux ne sont qu'une SOMME de présentation des lignes.
/// </summary>
internal sealed class TvaDeclarationConsoleQueryService : ITvaDeclarationConsoleQueries
{
    private readonly IMarginRegistryQueries _marginRegistryQueries;

    public TvaDeclarationConsoleQueryService(IMarginRegistryQueries marginRegistryQueries)
    {
        _marginRegistryQueries = marginRegistryQueries;
    }

    public async Task<TvaDeclarationViewModel> GetDeclarationAsync(string? period, CancellationToken cancellationToken = default)
    {
        var aggregates = await _marginRegistryQueries.GetMonthlyAsync(period, cancellationToken).ConfigureAwait(false);

        var lines = aggregates.Select(TvaDeclarationRow.FromDto).ToList();

        return new TvaDeclarationViewModel
        {
            Lines = lines,
            TotalBaseHt = lines.Sum(line => line.MarginBaseHt),
            TotalVat = lines.Sum(line => line.MarginVat),
        };
    }
}
