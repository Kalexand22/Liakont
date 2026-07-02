namespace Liakont.Host.Ged;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Host.Security;
using Liakont.Modules.Ged.Application.Index;
using Liakont.Modules.Ged.Contracts.Consultation;
using Stratum.Common.Abstractions.Security;

/// <summary>
/// Implémentation de la composition en lecture du portail GED (F19 §6.7). Consomme la surface existante du module :
/// l'index de recherche <see cref="IDocumentSearchIndex"/> (GED08), les permissions Liakont (GED06) et le journal de
/// consultation <see cref="IConsultationAuditWriter"/> (GED13). AUCUNE règle métier : projette les critères de la
/// page vers l'index et re-projette le résultat vers les modèles de vue du Host.
/// </summary>
/// <remarks>
/// Confidentialité (§6.5, anti-oracle) : le droit <c>liakont.ged.confidential</c> est résolu ICI, côté serveur,
/// depuis <see cref="IPermissionService"/> (les permissions ne sont pas exposées par le contexte d'acteur socle) et
/// transmis à l'index — le masquage server-side (axes, facettes, graphe) ne dépend JAMAIS d'un booléen fourni par
/// la page. Audit (§6.6) : chaque recherche écrit une entrée <c>action='search'</c> ; en régime probant
/// (<c>Evidential</c>, D8) une trace non écrite lève <see cref="ConsultationAuditException"/> que la page traduit en
/// refus d'accès (fail-closed) — on ne l'avale donc pas. En régime <c>BestEffort</c> (défaut) le writer n'échoue
/// jamais (échec journalisé en Warning). L'écriture suit la recherche : en régime probant, un échec de trace empêche
/// la page d'afficher des résultats (pas de fuite).
/// </remarks>
internal sealed class GedSearchQueryService : IGedQueries
{
    private readonly IDocumentSearchIndex _searchIndex;
    private readonly IConsultationAuditWriter _consultationAudit;
    private readonly IPermissionService _permissions;

    public GedSearchQueryService(
        IDocumentSearchIndex searchIndex,
        IConsultationAuditWriter consultationAudit,
        IPermissionService permissions)
    {
        _searchIndex = searchIndex;
        _consultationAudit = consultationAudit;
        _permissions = permissions;
    }

    public async Task<GedSearchResults> SearchAsync(GedSearchRequest request, CancellationToken cancellationToken = default)
    {
        // Droit de confidentialité résolu SERVER-SIDE (§6.5) : c'est le seul point qui décide du masquage — jamais
        // un paramètre venu de la page.
        var hasConfidentialRight = _permissions.HasPermission(LiakontPermissions.GedConfidential);

        var query = new DocumentSearchQuery
        {
            FullText = request.FullText,
            AxisFilters = request.AxisFilters.Select(f => new AxisFilter(f.AxisCode, f.Value)).ToList(),
            HasConfidentialRight = hasConfidentialRight,
            AfterManagedDocumentId = request.AfterDocumentId,
            PageSize = request.PageSize,
        };

        var result = await _searchIndex.SearchAsync(query, cancellationToken).ConfigureAwait(false);

        // Journal de consultation (§6.6, GED13) : APRÈS la recherche. En régime Evidential une trace non écrite
        // lève et empêche l'affichage (fail-closed, aucune fuite) ; en BestEffort le writer ne lève jamais.
        await WriteSearchConsultationAsync(request, hasConfidentialRight, result.Hits.Count, cancellationToken)
            .ConfigureAwait(false);

        return new GedSearchResults
        {
            Hits = result.Hits
                .Select(h => new GedSearchHit(h.ManagedDocumentId, h.Title, h.DocKind, h.Status))
                .ToList(),
            Facets = result.Facets
                .Select(f => new GedSearchFacet(f.AxisCode, f.Value, f.Count))
                .ToList(),
            NextCursor = result.NextCursor,
        };
    }

    private Task WriteSearchConsultationAsync(
        GedSearchRequest request,
        bool hasConfidentialRight,
        int resultCount,
        CancellationToken cancellationToken)
    {
        // Détail des critères (jsonb) : un axe peut porter PLUSIEURS valeurs (ex. acheteur=Dupont ET
        // acheteur=Martin — la page dédup sur la paire (code, valeur), pas sur l'axe seul). On sérialise
        // TOUTES les valeurs par axe (jamais dernier-gagne, qui perdrait un critère réel de la piste probante
        // §6.6). Le writer masque ensuite la valeur d'un axe confidentiel ciblé sans le droit (anti-oracle §6.5).
        Dictionary<string, string?>? detail = null;
        string[]? targetedAxisCodes = null;
        if (request.AxisFilters.Count > 0)
        {
            detail = request.AxisFilters
                .GroupBy(f => f.AxisCode, StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => (string?)string.Join(", ", g.Select(f => f.Value)),
                    StringComparer.Ordinal);

            targetedAxisCodes = [.. detail.Keys];
        }

        return _consultationAudit.WriteAsync(
            new ConsultationLogEntry
            {
                Action = ConsultationAction.Search,
                QueryText = request.FullText,
                ResultCount = resultCount,
                Detail = detail,
                TargetedAxisCodes = targetedAxisCodes,
                ActorHasConfidentialAccess = hasConfidentialRight,
            },
            cancellationToken);
    }
}
