namespace Liakont.Host.Documents;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Documents.Contracts.Queries;
using Microsoft.Extensions.Logging;

/// <summary>
/// Implémentation de <see cref="IDocumentConsoleQueries"/> : boucle sur la liste paginée serveur du module
/// Documents pour assembler le périmètre période COMPLET (aucune troncature). Aucune règle métier : les
/// résumés sont reportés tels quels. Tenant-scopée par construction (la connexion EST le tenant).
/// </summary>
internal sealed partial class DocumentConsoleQueryService : IDocumentConsoleQueries
{
    // Taille de page demandée = plafond du module (PostgresDocumentQueries.MaxPageSize). On boucle pour
    // récupérer toutes les pages : DeclaredListPage a besoin du périmètre complet (export/filtre/colonnes).
    private const int PageSize = 200;

    // Garde-fou anti-boucle (données mouvantes / total incohérent) : 2 M documents max sur une période.
    // Jamais atteint en pratique ; protège contre une boucle illimitée si TotalCount était corrompu.
    private const int MaxPages = 10_000;

    private readonly IDocumentQueries _documents;
    private readonly ILogger<DocumentConsoleQueryService> _logger;

    public DocumentConsoleQueryService(IDocumentQueries documents, ILogger<DocumentConsoleQueryService> logger)
    {
        _documents = documents;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DocumentSummaryDto>> GetDocumentsInPeriodAsync(
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken = default)
    {
        var first = await _documents
            .GetDocumentsAsync(
                new DocumentListFilter { From = from, To = to, Page = 1, PageSize = PageSize },
                cancellationToken)
            .ConfigureAwait(false);

        // Cas nominal : tout tient sur la première page (périmètre <= 200 documents).
        if (first.TotalCount <= first.Items.Count)
        {
            return first.Items;
        }

        var items = new List<DocumentSummaryDto>(first.TotalCount);
        items.AddRange(first.Items);

        // PageSize effectif = celui RENVOYÉ par le module (après bornage), pas celui demandé.
        var pageCount = (int)Math.Ceiling(first.TotalCount / (double)first.PageSize);
        if (pageCount > MaxPages)
        {
            // Cap atteint : l'incomplétude est TRACÉE (jamais silencieuse) — exigence de complétude fiscale.
            pageCount = MaxPages;
            LogLoadCapReached(_logger, first.TotalCount, MaxPages, PageSize);
        }

        for (var page = 2; page <= pageCount; page++)
        {
            var next = await _documents
                .GetDocumentsAsync(
                    new DocumentListFilter { From = from, To = to, Page = page, PageSize = PageSize },
                    cancellationToken)
                .ConfigureAwait(false);

            if (next.Items.Count == 0)
            {
                // Robustesse : moins de données que le total initial laissait croire (lecture concurrente
                // d'un état mouvant). On s'arrête proprement plutôt que de boucler à vide — et on le TRACE
                // si le périmètre rendu est incomplet (anti faux-vert : ne jamais présenter partiel = complet).
                if (items.Count < first.TotalCount)
                {
                    LogLoadStoppedEarly(_logger, items.Count, first.TotalCount);
                }

                break;
            }

            items.AddRange(next.Items);
        }

        return items;
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Documents list period scope exceeds the load cap ({TotalCount} documents, cap {MaxPages} pages of {PageSize}). The list is truncated; narrow the period.")]
    private static partial void LogLoadCapReached(ILogger logger, int totalCount, int maxPages, int pageSize);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Documents list load stopped early: {Loaded} of {TotalCount} documents (data changed mid-read).")]
    private static partial void LogLoadStoppedEarly(ILogger logger, int loaded, int totalCount);
}
