namespace Liakont.Host.Backfill;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Archive.Application;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Ged.Contracts.Backfill;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Jobs;

/// <summary>
/// Backfill rétroactif du corpus fiscal déjà scellé vers l'index GED, pour UN tenant (GED10, F19 §11 D12), exécuté
/// par <c>TenantJobRunner</c> (SOL06). ORCHESTRATION cross-module au COMPOSITION ROOT (seul endroit qui voit à la fois
/// l'archive fiscale ET la GED — la GED reste un silo, l'archive n'a pas le droit de référencer la GED) : énumère la
/// chaîne d'archives fiscales (<see cref="IArchiveEntryStore"/>), projette chaque entrée + son document
/// (<see cref="IDocumentQueries"/>) en requête plate, et la remet au point d'entrée d'indexation DIRECT de la GED
/// (<see cref="IGedArchivedDocumentBackfill"/>). Chemin DIRECT (hors-outbox), idempotent (clé = <c>archive_entry_id</c>),
/// jamais un effet de bord du flux fiscal (RL-21). Le module ne fait JAMAIS sa propre boucle multi-tenant
/// (module-rules §6) — le fan-out est la responsabilité du runner.
/// </summary>
public sealed partial class GedCorpusBackfillTenantJob : ITenantJob
{
    // Cadence du log de progression : sur un tenant à gros corpus scellé, le run est mono-thread avec un aller-retour
    // par entrée (N+1) ; un point d'avancement périodique donne à l'opérateur une visibilité (le run reste reprenable —
    // l'idempotence rend un re-lancement no-op sur les entrées déjà traitées).
    private const int ProgressLogEvery = 500;

    public string Name => "ged.corpus-backfill";

    public async Task ExecuteAsync(TenantJobContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var services = context.Services;
        var archiveEntries = services.GetRequiredService<IArchiveEntryStore>();
        var documents = services.GetRequiredService<IDocumentQueries>();
        var backfill = services.GetRequiredService<IGedArchivedDocumentBackfill>();
        var logger = services.GetRequiredService<ILogger<GedCorpusBackfillTenantJob>>();

        // GetChainAsync renvoie l'INTÉGRALITÉ de la chaîne du tenant en un instantané cohérent (ordre déterministe) :
        // pas de pagination/OFFSET à faire dériver, donc pas de risque de saut d'entrées pendant le parcours.
        var chain = await archiveEntries.GetChainAsync(cancellationToken);

        var indexed = 0;
        var deferred = 0;
        var alreadyPresent = 0;
        var skippedMissingDocument = 0;
        var processed = 0;

        foreach (var entry in chain)
        {
            processed++;
            if (processed % ProgressLogEvery == 0)
            {
                LogProgress(logger, context.TenantId, processed, chain.Count);
            }

            var document = await documents.GetByIdAsync(entry.DocumentId, cancellationToken);
            if (document is null)
            {
                // Entrée de coffre sans document lisible dans ce tenant : on N'INVENTE PAS un type/des champs
                // (jamais deviner, règle 2) — on journalise et on passe (l'index GED ne fabrique pas de faux).
                skippedMissingDocument++;
                LogMissingDocument(logger, entry.EntryId, entry.DocumentId);
                continue;
            }

            var request = new GedBackfillDocumentRequest(
                ArchiveEntryId: entry.EntryId,
                FiscalDocumentId: entry.DocumentId,
                ArchivePath: entry.PackagePath,
                ContentHash: entry.PackageHash,
                DocumentType: document.DocumentType,
                SourceReference: document.SourceReference,
                SourceFields: BuildSourceFields(document));

            var outcome = await backfill.BackfillAsync(request, cancellationToken);
            switch (outcome)
            {
                case GedBackfillOutcome.Indexed:
                    indexed++;
                    break;
                case GedBackfillOutcome.Deferred:
                    deferred++;
                    break;
                default:
                    alreadyPresent++;
                    break;
            }
        }

        LogBackfilled(logger, context.TenantId, chain.Count, indexed, deferred, alreadyPresent, skippedMissingDocument);
    }

    // Projection PLATE des champs BRUTS du document fiscal offerts au mapping déclaratif GED. Noms GÉNÉRIQUES (aucun
    // vocabulaire métier) ; montants en chaîne invariante (« . » décimal) — leur interprétation numérique éventuelle
    // relève du profil GED (decimal half-up), jamais d'un calcul ici (règle 1). Champs absents = OMIS (symétrie null).
    private static Dictionary<string, string> BuildSourceFields(DocumentDto document)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["documentNumber"] = document.DocumentNumber,
            ["documentType"] = document.DocumentType,
            ["issueDate"] = document.IssueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["state"] = document.State,
            ["totalNet"] = document.TotalNet.ToString(CultureInfo.InvariantCulture),
            ["totalTax"] = document.TotalTax.ToString(CultureInfo.InvariantCulture),
            ["totalGross"] = document.TotalGross.ToString(CultureInfo.InvariantCulture),
        };

        if (!string.IsNullOrWhiteSpace(document.SupplierSiren))
        {
            fields["supplierSiren"] = document.SupplierSiren;
        }

        if (!string.IsNullOrWhiteSpace(document.CustomerName))
        {
            fields["customerName"] = document.CustomerName;
        }

        return fields;
    }

    [LoggerMessage(EventId = 7320, Level = LogLevel.Warning,
        Message = "Backfill GED : entrée de coffre {EntryId} sans document fiscal {DocumentId} dans le tenant — ignorée (jamais deviner).")]
    private static partial void LogMissingDocument(ILogger logger, Guid entryId, Guid documentId);

    [LoggerMessage(EventId = 7322, Level = LogLevel.Information,
        Message = "Backfill GED du tenant « {TenantId} » : {Processed}/{Total} entrée(s) traitée(s)…")]
    private static partial void LogProgress(ILogger logger, string tenantId, int processed, int total);

    [LoggerMessage(EventId = 7321, Level = LogLevel.Information,
        Message = "Backfill GED du tenant « {TenantId} » : {Total} entrée(s) — {Indexed} indexée(s), {Deferred} déférée(s), {AlreadyPresent} déjà présente(s), {Skipped} ignorée(s).")]
    private static partial void LogBackfilled(ILogger logger, string tenantId, int total, int indexed, int deferred, int alreadyPresent, int skipped);
}
