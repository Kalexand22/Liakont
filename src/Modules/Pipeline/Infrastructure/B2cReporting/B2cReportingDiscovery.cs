namespace Liakont.Modules.Pipeline.Infrastructure.B2cReporting;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Pipeline.Infrastructure.Check;
using Liakont.Modules.Pipeline.Infrastructure.Serialization;
using Liakont.Modules.Staging.Contracts;
using Liakont.Modules.TvaMapping.Contracts.Services;
using Microsoft.Extensions.Logging;

/// <summary>
/// Helpers de DÉCOUVERTE PARTAGÉS aux jobs agrégés e-reporting B2C (marge <see cref="B2cMarginAggregatorTenantJob"/>
/// et prix total <see cref="B2cTaxableAggregatorTenantJob"/>) : relecture du pivot SOURCE stagé et
/// ré-enrichissement par le mapping TVA validé (qui DÉRIVE les marqueurs B2C). Logique générique (aucun régime
/// spécifique) ; la discrimination marge ↔ taxable se fait ensuite par les prédicats
/// <c>B2cMarginDeclaration</c>/<c>B2cTaxableDeclaration</c>, côté job.
/// </summary>
internal static partial class B2cReportingDiscovery
{
    /// <summary>
    /// Frais d'enchères présents (acheteur OU vendeur) : pré-filtre cheap — seuls les documents porteurs de
    /// frais peuvent être une déclaration B2C agrégée (<c>B2cAggregatedDeclaration</c> l'exige). Inutile de
    /// mapper les autres.
    /// </summary>
    /// <param name="pivot">Le pivot à pré-filtrer.</param>
    /// <returns><c>true</c> si des frais acheteur/vendeur sont présents.</returns>
    public static bool HasFees(PivotDocumentDto pivot) =>
        Domain.B2cReporting.B2cAuctionFeeLines.HasAuctionFees(pivot);

    /// <summary>
    /// Lit le pivot stagé d'un document (PIP00). NotFound (non/plus stagé) → <c>null</c> ce cycle (transitoire) ;
    /// Integrity (contenu altéré) → <c>null</c> + journalisé (jamais traiter un contenu altéré — CLAUDE.md n°3).
    /// </summary>
    /// <param name="staging">Le store de staging.</param>
    /// <param name="tenantId">Le tenant courant.</param>
    /// <param name="document">Le document (clé : id + empreinte de payload).</param>
    /// <param name="logger">Le journal applicatif.</param>
    /// <param name="cancellationToken">Le jeton d'annulation.</param>
    /// <returns>Le pivot SOURCE stagé, ou <c>null</c> (ignoré ce cycle).</returns>
    public static async Task<PivotDocumentDto?> TryReadStagedPivotAsync(
        IPayloadStagingStore staging,
        string tenantId,
        DocumentDto document,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var key = new StagedPayloadKey(tenantId, document.Id, document.PayloadHash);
            var json = await staging.ReadAsync(key, cancellationToken);
            return PivotCanonicalJsonReader.Read(json);
        }
        catch (StagedPayloadNotFoundException)
        {
            return null;
        }
        catch (StagedPayloadIntegrityException)
        {
            LogStagingIntegrity(logger, document.Id);
            return null;
        }
    }

    /// <summary>
    /// Enrichit le pivot SOURCE par le mapping TVA validé (catégorie + VATEX) via le MÊME moteur qu'au CHECK/SEND
    /// (<see cref="CheckTvaMapping"/>), qui DÉRIVE les marqueurs de déclaration B2C (marge ET prix total) sur le
    /// pivot enrichi. Retourne <c>null</c> si aucune ligne mappable ou si le mapping bloque (table absente /
    /// régime devenu non couvert depuis le CHECK) : document ignoré ce cycle, repris au suivant — jamais marqué à
    /// l'aveugle (CLAUDE.md n°2/3).
    /// </summary>
    /// <param name="tvaMapping">Le service de mapping TVA du tenant.</param>
    /// <param name="companyId">L'identité fiscale du tenant.</param>
    /// <param name="pivot">Le pivot source à enrichir.</param>
    /// <param name="cancellationToken">Le jeton d'annulation.</param>
    /// <returns>Le pivot enrichi + marqué, ou <c>null</c> (non mappable ce cycle).</returns>
    public static async Task<PivotDocumentDto?> EnrichForB2cMarkingAsync(
        ITvaMappingService tvaMapping,
        Guid companyId,
        PivotDocumentDto pivot,
        CancellationToken cancellationToken)
    {
        var plan = CheckTvaMapping.BuildPlan(pivot);
        if (plan.Requests.Count == 0)
        {
            return null; // aucune ligne de forme mappable → aucun signal de régime dérivable.
        }

        var mapping = await tvaMapping.MapAsync(companyId, plan.Requests, cancellationToken);
        if (!mapping.TableExists)
        {
            return null; // aucune table de mapping (supprimée depuis le CHECK) → pas de classification, jamais devinée.
        }

        var evaluation = CheckTvaMapping.Evaluate(pivot, plan, mapping);
        return evaluation.IsBlocked ? null : evaluation.EnrichedDocument;
    }

    [LoggerMessage(EventId = 7461, Level = LogLevel.Warning,
        Message = "E-reporting B2C : pivot stagé altéré pour le document « {DocumentId} » — ignoré (jamais traiter un contenu altéré).")]
    private static partial void LogStagingIntegrity(ILogger logger, Guid documentId);
}
