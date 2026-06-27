namespace Liakont.Modules.Pipeline.Infrastructure.B2cReporting;

using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Pipeline.Contracts;
using Liakont.Modules.Pipeline.Contracts.Queries;
using Liakont.Modules.Pipeline.Domain.B2cReporting;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Liakont.Modules.TvaMapping.Contracts.Services;
using MediatR;

/// <summary>
/// Handler du récap de marge d'un document (aide à la déclaration de TVA du détail). RÉUTILISE les cœurs partagés —
/// <see cref="B2cMarginMarking.IsMarginRegime"/> (buyer-indépendant : B2C ET B2B), <see cref="B2cMarginDocumentResolver"/>
/// (assemblage acheteur+vendeur + taux), <see cref="B2cTransactionAggregationCalculator.ToHt"/> (TTC→HT) — donc le
/// récap affiché est EXACTEMENT le calcul de l'e-reporting B2C (jamais une logique parallèle qui dériverait, P1).
/// Lecture seule, TENANT-SCOPÉE (société courante via <see cref="ITenantSettingsQueries.GetCurrentCompanyId"/>).
/// Fail-closed : document non-marge, société absente, ou marge bloquée → <c>null</c> (aucun chiffre deviné).
/// </summary>
public sealed class GetDocumentMarginRecapHandler : IRequestHandler<GetDocumentMarginRecapQuery, DocumentMarginRecapDto?>
{
    private readonly ITvaMappingService _tvaMapping;
    private readonly ITenantSettingsQueries _tenantSettings;

    /// <summary>Construit le handler du récap de marge.</summary>
    /// <param name="tvaMapping">Service de mapping TVA du tenant (taux des honoraires).</param>
    /// <param name="tenantSettings">Lectures tenant (société courante).</param>
    public GetDocumentMarginRecapHandler(ITvaMappingService tvaMapping, ITenantSettingsQueries tenantSettings)
    {
        _tvaMapping = tvaMapping;
        _tenantSettings = tenantSettings;
    }

    /// <inheritdoc />
    public async Task<DocumentMarginRecapDto?> Handle(GetDocumentMarginRecapQuery request, CancellationToken cancellationToken)
    {
        var pivot = request.Pivot;

        // Régime de la marge buyer-indépendant (B2C ou B2B) : sinon pas de récap (document taxable / ordinaire).
        if (!B2cMarginMarking.IsMarginRegime(pivot))
        {
            return null;
        }

        var companyId = await _tenantSettings.GetCurrentCompanyId(cancellationToken).ConfigureAwait(false);
        if (companyId is null)
        {
            return null;
        }

        var resolution = await B2cMarginDocumentResolver
            .ResolveAsync(_tvaMapping, companyId.Value, pivot, cancellationToken)
            .ConfigureAwait(false);

        // Fail-closed : marge non résolvable (TVA distincte, taux non mappé/mixte) → pas de récap (jamais deviné).
        if (!resolution.IsResolved)
        {
            return null;
        }

        var (baseHt, tva) = B2cTransactionAggregationCalculator.ToHt(resolution.MarginTtc, resolution.RatePercent);

        return new DocumentMarginRecapDto
        {
            BuyerFeesTtc = resolution.BuyerFeesTtc,
            SellerFeesTtc = resolution.SellerFeesTtc,
            MarginTtc = resolution.MarginTtc,
            BaseHt = baseHt,
            Tva = tva,
            RatePercent = resolution.RatePercent,
        };
    }
}
