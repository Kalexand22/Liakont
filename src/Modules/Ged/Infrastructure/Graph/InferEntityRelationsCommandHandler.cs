namespace Liakont.Modules.Ged.Infrastructure.Graph;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Ged.Application;
using Liakont.Modules.Ged.Application.Graph;
using Liakont.Modules.Ged.Contracts.Commands;
using Liakont.Modules.Ged.Domain.Graph;
using Liakont.Modules.Ged.Domain.Index;
using MediatR;

/// <summary>
/// Handler d'inférence/héritage de relations GED (F19 §10, GED24). Séquence : (1) charger les règles tenant
/// ACTIVES (aucune → rien à faire, 0) ; (2) charger le voisinage ASSERTÉ borné de la graine (profondeur = max des
/// bornes déclarées, anti-DoS) + ses relations déjà courantes (idempotence) ; (3) calculer les relations dérivées
/// via le moteur PUR <see cref="RelationInferenceEngine"/> ; (4) les APPENDER (append-only, une transaction).
/// Rend le nombre de relations ajoutées. Tenant-scopé par la connexion (INV-GED-08).
/// </summary>
internal sealed class InferEntityRelationsCommandHandler : IRequestHandler<InferEntityRelationsCommand, int>
{
    private readonly IRelationInferenceRuleStore _ruleStore;
    private readonly IEntityRelationGraphReader _graphReader;
    private readonly IGedIndexUnitOfWorkFactory _unitOfWorkFactory;

    public InferEntityRelationsCommandHandler(
        IRelationInferenceRuleStore ruleStore,
        IEntityRelationGraphReader graphReader,
        IGedIndexUnitOfWorkFactory unitOfWorkFactory)
    {
        _ruleStore = ruleStore;
        _graphReader = graphReader;
        _unitOfWorkFactory = unitOfWorkFactory;
    }

    public async Task<int> Handle(InferEntityRelationsCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var rules = await _ruleStore.GetActiveRulesAsync(cancellationToken);
        if (rules.Count == 0)
        {
            return 0; // aucune règle tenant : l'inférence est un no-op (rien inventé, règle 2).
        }

        // Borne globale d'exploration = la plus grande borne déclarée (chaque règle re-borne ensuite précisément).
        var maxDepth = rules.Max(r => r.MaxDepth);

        var neighbourhood = await _graphReader.LoadAssertedNeighbourhoodAsync(
            request.SeedEntityId, maxDepth, cancellationToken);
        var existingOut = await _graphReader.LoadCurrentOutRelationsAsync(
            request.SeedEntityId, cancellationToken);

        var derived = RelationInferenceEngine.Infer(request.SeedEntityId, neighbourhood, existingOut, rules);
        if (derived.Count == 0)
        {
            return 0;
        }

        await using var unitOfWork = await _unitOfWorkFactory.BeginAsync(cancellationToken);

        foreach (var relation in derived)
        {
            // Relation dérivée déterministe → confidence_score null (aucun score inventé) ; canal = request.Source
            // (validé contre le vocabulaire fermé par le constructeur Domain).
            var entityRelation = new EntityRelation(
                relation.FromEntityId,
                relation.ToEntityId,
                relation.RelationKind,
                relation.RelationType,
                request.Source);

            await unitOfWork.AppendRelationAsync(entityRelation, cancellationToken);
        }

        await unitOfWork.CommitAsync(cancellationToken);
        return derived.Count;
    }
}
