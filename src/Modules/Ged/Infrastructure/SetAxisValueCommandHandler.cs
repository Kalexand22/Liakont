namespace Liakont.Modules.Ged.Infrastructure;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Ged.Application;
using Liakont.Modules.Ged.Application.Index;
using Liakont.Modules.Ged.Contracts.Commands;
using Liakont.Modules.Ged.Domain.Catalog;
using Liakont.Modules.Ged.Domain.Index;
using MediatR;

/// <summary>
/// Handler d'écriture d'une valeur d'axe GED (F19 §3.7). Séquence : (1) résoudre l'axe par son code via
/// <see cref="IAxisCatalog"/> — refus si inconnu/inactif (jamais deviner, règle 2) ; (2) normaliser la valeur
/// brute (<see cref="ValueNormalizer"/>, Domain pur) vers sa colonne typée — refus si elle ne correspond pas au
/// <c>data_type</c>, et, pour un axe <c>enum</c>, refus si hors du vocabulaire déclaré ; (3) appender le lien sous
/// garde de concurrence mono-valeur (RL-02), supersession chaînée dans la même transaction. Tenant-scopé par la
/// connexion (n°9). (4) si l'axe est <c>searchable</c>, RE-PROJETER le dérivé <c>document_search</c> APRÈS le commit
/// (GED08 / F19 §6.1) : sans cela le <c>search_vector</c> reste figé à l'ingestion et une correction opérateur
/// (« Beta »→« Alpha ») est introuvable en plein-texte, l'ancienne valeur supersédée continuant de remonter le
/// document indéfiniment (règle 4 : le dérivé reconstructible n'est pas une mutation d'audit ; un axe non searchable
/// n'entraîne aucune re-projection inutile).
/// </summary>
internal sealed class SetAxisValueCommandHandler : IRequestHandler<SetAxisValueCommand, Guid>
{
    private readonly IAxisCatalog _axisCatalog;
    private readonly IGedIndexUnitOfWorkFactory _unitOfWorkFactory;
    private readonly IDocumentSearchIndex _documentSearchIndex;

    public SetAxisValueCommandHandler(
        IAxisCatalog axisCatalog,
        IGedIndexUnitOfWorkFactory unitOfWorkFactory,
        IDocumentSearchIndex documentSearchIndex)
    {
        _axisCatalog = axisCatalog;
        _unitOfWorkFactory = unitOfWorkFactory;
        _documentSearchIndex = documentSearchIndex;
    }

    public async Task<Guid> Handle(SetAxisValueCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var axis = await _axisCatalog.ResolveAsync(request.AxisCode, cancellationToken);
        if (axis is null || !axis.IsActive)
        {
            throw new AxisNotResolvableException(request.AxisCode, request.DocumentId);
        }

        var normalized = ValueNormalizer.Normalize(axis.DataType, axis.ValueScale, request.RawValue);

        if (axis.DataType == AxisDataType.Enum
            && !axis.AllowedEnumValues.Contains(normalized.ValueString!, StringComparer.Ordinal))
        {
            throw new AxisValueFormatException(
                AxisDataType.Enum,
                request.RawValue,
                $"« {request.RawValue} » n'appartient pas au vocabulaire déclaré de l'axe « {request.AxisCode} ».");
        }

        var link = new DocumentAxisLink(
            request.DocumentId,
            axis.Id,
            normalized,
            request.Source,
            request.ConfidenceScore,
            request.OperatorIdentity);

        await using var unitOfWork = await _unitOfWorkFactory.BeginAsync(cancellationToken);
        var id = await unitOfWork.AppendAxisLinkAsync(link, isSingleValued: !axis.IsMultiValue, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        // Re-projection du dérivé document_search (GED08, F19 §6.1, règle 4). APRÈS le commit : RefreshDocumentAsync
        // lit current_axis_links via une connexion distincte, il ne voit donc que du committé (une re-projection avant
        // commit rendrait un vecteur figé sur l'ancienne valeur). Ne se déclenche que pour un axe searchable : le
        // search_vector n'agrège que les axes searchables, un axe non searchable ne changerait rien (pas de dérivé
        // inutile). Idempotent (UPSERT) et reconstructible : ce refresh n'est pas une mutation d'audit.
        if (axis.IsSearchable)
        {
            await _documentSearchIndex.RefreshDocumentAsync(request.DocumentId, cancellationToken);
        }

        return id;
    }
}
