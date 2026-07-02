namespace Liakont.Host.CountryReference;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Reference.Contracts;

/// <summary>
/// Implémentation de <see cref="ICountryAliasConsoleService"/> pour l'écran « Référentiel pays » (ADR-0038).
/// Projette les correspondances du référentiel (consommé par son <c>Contracts</c> uniquement — frontière
/// inter-modules, CLAUDE.md n°14) en <see cref="CountryAliasRow"/>. Projection PURE (formatage) : aucune règle
/// métier, aucune mutation (les écritures passent par les commandes MediatR, jamais par ce service).
/// </summary>
internal sealed class CountryAliasConsoleService : ICountryAliasConsoleService
{
    private readonly ICountryAliasReferential _referential;

    public CountryAliasConsoleService(ICountryAliasReferential referential)
    {
        _referential = referential;
    }

    public async Task<IReadOnlyList<CountryAliasRow>> ListAsync(CancellationToken cancellationToken = default)
    {
        var aliases = await _referential.GetAliasesAsync(cancellationToken).ConfigureAwait(false);
        if (aliases.Count == 0)
        {
            return [];
        }

        var rows = new List<CountryAliasRow>(aliases.Count);
        foreach (var alias in aliases)
        {
            rows.Add(new CountryAliasRow
            {
                SourceCode = alias.SourceCode,
                IsoCode = alias.IsoCode,
                UpdatedAtUtc = alias.UpdatedAtUtc,
            });
        }

        return rows;
    }
}
