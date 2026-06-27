namespace Liakont.Host.B2cReporting;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Pipeline.Contracts.Queries;

/// <summary>
/// Implémentation de <see cref="IB2cMarginEmissionsConsoleQueries"/> : lit le journal d'émission de la marge
/// regroupé par agrégat (<see cref="IB2cMarginEmissionQueries"/>, B4) et projette en
/// <see cref="B2cMarginEmissionsViewModel"/>. Lecture tenant-scopée par construction (base du tenant courant
/// — CLAUDE.md n°9/17). AUCUNE règle fiscale n'est dérivée : l'état d'émission vient de B4, reporté tel quel
/// (CLAUDE.md n°2).
/// </summary>
internal sealed class B2cMarginEmissionsConsoleQueryService : IB2cMarginEmissionsConsoleQueries
{
    private readonly IB2cMarginEmissionQueries _emissionQueries;

    public B2cMarginEmissionsConsoleQueryService(IB2cMarginEmissionQueries emissionQueries)
    {
        _emissionQueries = emissionQueries;
    }

    public async Task<B2cMarginEmissionsViewModel> GetEmissionsAsync(string? period, CancellationToken cancellationToken = default)
    {
        var emissions = await _emissionQueries.GetEmissionsAsync(period, cancellationToken).ConfigureAwait(false);

        return new B2cMarginEmissionsViewModel
        {
            Emissions = emissions.Select(B2cMarginEmissionRow.FromDto).ToList(),
        };
    }

    public async Task<B2cMarginEmissionDetailViewModel?> GetEmissionDetailAsync(Guid emissionBatchId, CancellationToken cancellationToken = default)
    {
        var detail = await _emissionQueries.GetEmissionDetailAsync(emissionBatchId, cancellationToken).ConfigureAwait(false);
        return detail is null ? null : B2cMarginEmissionDetailViewModel.FromDto(detail);
    }
}
