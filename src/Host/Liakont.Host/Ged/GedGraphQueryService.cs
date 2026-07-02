namespace Liakont.Host.Ged;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Host.Security;
using Liakont.Modules.Ged.Application.Index;
using Liakont.Modules.Ged.Contracts.Consultation;
using Stratum.Common.Abstractions.Security;

/// <summary>
/// Implémentation de la composition en lecture de l'exploration de graphe GED (F19 §6.7). Consomme la surface
/// existante du module : l'index de graphe <see cref="IDocumentSearchIndex.ExploreGraphAsync"/> (GED08), les
/// permissions Liakont (GED06) et le journal de consultation <see cref="IConsultationAuditWriter"/> (GED13).
/// AUCUNE règle métier : projette les critères de la page vers l'index et re-projette le résultat vers les modèles
/// de vue du Host.
/// </summary>
/// <remarks>
/// Confidentialité (§6.4/§6.5, anti-oracle) : le droit <c>liakont.ged.confidential</c> est résolu ICI, côté serveur,
/// depuis <see cref="IPermissionService"/> (les permissions ne sont pas exposées par le contexte d'acteur socle) et
/// transmis à l'index — la traversée exclut la racine ET les voisins dont le type est confidentiel sans le droit
/// (racine confidentielle → ensemble vide, pas d'oracle depth-0, RL-31). Le masquage ne dépend JAMAIS d'un booléen
/// fourni par la page. Audit (§6.6) : chaque exploration écrit une entrée <c>action='explore_entity'</c> portant
/// l'entité racine ; en régime probant (<c>Evidential</c>, D8) une trace non écrite lève
/// <see cref="ConsultationAuditException"/> que la page traduit en refus d'accès (fail-closed) — on ne l'avale donc
/// pas. En régime <c>BestEffort</c> (défaut) le writer n'échoue jamais (échec journalisé en Warning).
/// </remarks>
internal sealed class GedGraphQueryService : IGedGraphQueries
{
    private readonly IDocumentSearchIndex _searchIndex;
    private readonly IConsultationAuditWriter _consultationAudit;
    private readonly IPermissionService _permissions;

    public GedGraphQueryService(
        IDocumentSearchIndex searchIndex,
        IConsultationAuditWriter consultationAudit,
        IPermissionService permissions)
    {
        _searchIndex = searchIndex;
        _consultationAudit = consultationAudit;
        _permissions = permissions;
    }

    public async Task<GedGraphResults> ExploreAsync(GedGraphRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Droit de confidentialité résolu SERVER-SIDE (§6.4/§6.5) : c'est le seul point qui décide de l'exclusion —
        // jamais un paramètre venu de la page.
        var hasConfidentialRight = _permissions.HasPermission(LiakontPermissions.GedConfidential);

        var query = new GraphExplorationQuery
        {
            RootEntityId = request.RootEntityId,
            MaxDepth = request.MaxDepth,
            HasConfidentialRight = hasConfidentialRight,
            After = request.After is { } cursor
                ? new GraphCursor(cursor.ManagedDocumentId, cursor.EntityId, cursor.Role)
                : null,
            PageSize = request.PageSize,
        };

        var result = await _searchIndex.ExploreGraphAsync(query, cancellationToken).ConfigureAwait(false);

        // Journal de consultation (§6.6, GED13) : APRÈS l'exploration. En régime Evidential une trace non écrite
        // lève et empêche l'affichage (fail-closed, aucune fuite) ; en BestEffort le writer ne lève jamais.
        await WriteExploreConsultationAsync(request, hasConfidentialRight, result.Documents.Count, cancellationToken)
            .ConfigureAwait(false);

        return new GedGraphResults
        {
            Hits = result.Documents
                .Select(d => new GedGraphHit(d.ManagedDocumentId, d.EntityId, d.Role, d.Depth))
                .ToList(),
            NextCursor = result.NextCursor is { } next
                ? new GedGraphCursor(next.ManagedDocumentId, next.EntityId, next.Role)
                : null,
        };
    }

    private Task WriteExploreConsultationAsync(
        GedGraphRequest request,
        bool hasConfidentialRight,
        int resultCount,
        CancellationToken cancellationToken)
    {
        // Le writer masque server-side (query_text/detail) si le type d'entité ciblé est RÉELLEMENT confidentiel
        // et que l'acteur n'a pas le droit (§6.5) : on lui passe le type ciblé et le droit, jamais une valeur en clair.
        return _consultationAudit.WriteAsync(
            new ConsultationLogEntry
            {
                Action = ConsultationAction.ExploreEntity,
                EntityId = request.RootEntityId,
                TargetedEntityTypeCode = request.EntityTypeCode,
                ResultCount = resultCount,
                ActorHasConfidentialAccess = hasConfidentialRight,
            },
            cancellationToken);
    }
}
