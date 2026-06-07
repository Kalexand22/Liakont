namespace Liakont.Modules.Archive.Application;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Archive.Contracts;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Liakont.Modules.TvaMapping.Contracts.Queries;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Modules.Audit.Contracts.Queries;

/// <summary>
/// Construit le dossier de RÉVERSIBILITÉ complet du tenant courant (F12 §6.3 ; <c>blueprint.md</c> §6 :
/// export/réversibilité = responsabilité Archive). Agrège, en ne franchissant que des <c>Contracts</c>
/// (frontière inter-modules, CLAUDE.md n°14) :
/// <list type="bullet">
///   <item>le coffre d'archive ENTIER (via <see cref="IFiscalControlExportService"/>, sous <c>archive/</c>) ;</item>
///   <item>le suivi des documents (tracking : chaque document + sa piste d'audit append-only), paginé par lots ;</item>
///   <item>le paramétrage du tenant (profil, fiscal, comptes PA — clés API TOUJOURS masquées, table TVA,
///         planification, seuils) ;</item>
///   <item>le journal d'audit opérateur (les 500 entrées les plus récentes — limite de la surface
///         <see cref="IAuditQueries.SearchEntries"/> ; signalée dans la notice, jamais masquée en silence).</item>
/// </list>
/// Tenant-scopé : toutes les lectures s'exécutent sur la base du tenant courant. La variante
/// <see cref="StreamAsync"/> produit les fichiers PARESSEUSEMENT (coffre lu pièce par pièce, tracking par
/// lots) pour ne jamais charger tout le coffre ni tous les documents en mémoire (anti-OOM).
/// </summary>
public sealed class TenantReversibilityExportService : ITenantReversibilityExportService
{
    /// <summary>Plafond du journal opérateur, aligné sur <see cref="IAuditQueries.SearchEntries"/> (LIMIT 500).</summary>
    private const int AuditJournalCap = 500;

    /// <summary>Taille de page (lot) du balayage des documents (tracking) — borne la mémoire par fichier.</summary>
    private const int TrackingPageSize = 200;

    private static readonly string ReversibilityNotice =
        $$"""
        NOTICE DE RÉVERSIBILITÉ — DOSSIER COMPLET DU TENANT (LIAKONT)
        ============================================================

        Ce dossier est l'export de RÉVERSIBILITÉ du tenant (F12 §6.3) : la matière que vous emportez
        si vous quittez la plateforme. Il réunit, pour votre seul tenant :

        CONTENU DU DOSSIER
        ------------------
        - archive/...                 : le coffre d'archive WORM entier (paquets, preuves d'ancrage,
                                        rapport d'intégrité archive/rapport-integrite.json, notice de
                                        vérification fiscale). C'est l'enregistrement AUTORITAIRE, complet
                                        et immuable.
        - tracking/documents-NNNN.json : les documents du tenant par lots et, pour chacun, sa piste d'audit
                                        append-only (DocumentEvents). tracking/index.json récapitule.
        - parametrage/profil.json     : profil du tenant (raison sociale, SIREN…).
        - parametrage/fiscal.json     : paramétrage fiscal.
        - parametrage/comptes-pa.json : comptes Plateforme Agréée — les clés API ne sont JAMAIS exportées
                                        (seul l'indicateur « clé saisie » figure).
        - parametrage/table-tva.json  : table de mapping TVA + état de validation.
        - parametrage/planification.json, parametrage/seuils-alerte.json : planification et seuils.
        - journal/audit.json          : journal d'audit opérateur.

        LIMITES ASSUMÉES
        ----------------
        1. Le journal d'audit opérateur (journal/audit.json) contient au plus les {{AuditJournalCap}} entrées
           les plus récentes (limite de l'interface de lecture).
        2. La section tracking/ est un INSTANTANÉ « meilleur effort » de l'état vivant des documents :
           pris pendant une ingestion concurrente, un document peut être omis du tracking. L'enregistrement
           AUTORITAIRE, complet et immuable reste le coffre WORM (archive/) et la piste d'audit fiscale
           qu'il scelle. Pour un tracking garanti complet, exporter pendant une fenêtre sans ingestion.
        """;

    private static readonly JsonSerializerOptions ExportJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IFiscalControlExportService _fiscalExport;
    private readonly IDocumentQueries _documentQueries;
    private readonly ITenantSettingsQueries _settingsQueries;
    private readonly ITvaMappingQueries _tvaQueries;
    private readonly IAuditQueries _auditQueries;
    private readonly ITenantContext _tenantContext;

    public TenantReversibilityExportService(
        IFiscalControlExportService fiscalExport,
        IDocumentQueries documentQueries,
        ITenantSettingsQueries settingsQueries,
        ITvaMappingQueries tvaQueries,
        IAuditQueries auditQueries,
        ITenantContext tenantContext)
    {
        _fiscalExport = fiscalExport;
        _documentQueries = documentQueries;
        _settingsQueries = settingsQueries;
        _tvaQueries = tvaQueries;
        _auditQueries = auditQueries;
        _tenantContext = tenantContext;
    }

    public async Task<TenantReversibilityExport> BuildAsync(CancellationToken cancellationToken = default)
    {
        var files = new List<FiscalExportFile>();
        await foreach (FiscalExportFile file in StreamAsync(cancellationToken))
        {
            files.Add(file);
        }

        List<FiscalExportFile> ordered = files
            .OrderBy(f => f.Path, StringComparer.Ordinal)
            .ToList();

        return new TenantReversibilityExport(ordered, ReversibilityNotice);
    }

    public async IAsyncEnumerable<FiscalExportFile> StreamAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        RequireTenant();

        // 1. Coffre d'archive entier (flux paresseux de l'export contrôle fiscal, préfixé sous archive/).
        //    Inclut archive/rapport-integrite.json (l'intégrité du coffre au moment de l'export).
        await foreach (FiscalExportFile file in _fiscalExport.StreamForRangeAsync(null, null, cancellationToken))
        {
            yield return file with { Path = "archive/" + file.Path };
        }

        // 2. Tracking : documents + piste d'audit, par lots (mémoire bornée), dédupliqués par identifiant.
        await foreach (FiscalExportFile file in StreamTrackingAsync(cancellationToken))
        {
            yield return file;
        }

        // 3. Paramétrage du tenant.
        foreach (FiscalExportFile file in await BuildSettingsFilesAsync(cancellationToken))
        {
            yield return file;
        }

        // 4. Journal d'audit opérateur (plafonné, signalé dans la notice).
        yield return await BuildAuditJournalFileAsync(cancellationToken);

        // 5. Notice de réversibilité.
        yield return new FiscalExportFile("notice-reversibilite.txt", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(ReversibilityNotice));
    }

    private static FiscalExportFile JsonFile(string path, object? payload) =>
        new(path, "application/json", JsonSerializer.SerializeToUtf8Bytes(payload, ExportJsonOptions));

    private async IAsyncEnumerable<FiscalExportFile> StreamTrackingAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var seen = new HashSet<Guid>();
        int page = 1;
        int scanned = 0;
        while (true)
        {
            DocumentListResult result = await _documentQueries.GetDocumentsAsync(
                new DocumentListFilter { Page = page, PageSize = TrackingPageSize },
                cancellationToken);

            if (result.Items.Count == 0)
            {
                break;
            }

            var pageDocuments = new List<object>();
            foreach (DocumentSummaryDto summary in result.Items)
            {
                if (!seen.Add(summary.Id))
                {
                    // Déjà exporté (réordonnancement sous écriture concurrente) : jamais en double.
                    continue;
                }

                IReadOnlyList<DocumentEventDto> events = await _documentQueries.GetEventsAsync(summary.Id, cancellationToken);
                pageDocuments.Add(new { document = summary, events });
            }

            if (pageDocuments.Count > 0)
            {
                yield return JsonFile(
                    $"tracking/documents-{page.ToString("D4", System.Globalization.CultureInfo.InvariantCulture)}.json",
                    new { page, count = pageDocuments.Count, documents = pageDocuments });
            }

            scanned += result.Items.Count;
            if (scanned >= result.TotalCount)
            {
                break;
            }

            page++;
        }

        // Index récapitulatif : présent même pour un tenant sans document (dossier non ambigu).
        yield return JsonFile("tracking/index.json", new { totalDocuments = seen.Count, pageSize = TrackingPageSize });
    }

    private async Task<IReadOnlyList<FiscalExportFile>> BuildSettingsFilesAsync(CancellationToken cancellationToken)
    {
        var files = new List<FiscalExportFile>();
        Guid? companyId = await _settingsQueries.GetCurrentCompanyId(cancellationToken);

        if (companyId is not { } company)
        {
            // Profil du tenant non encore créé (CFG02) : on exporte un marqueur explicite plutôt qu'un trou silencieux.
            files.Add(JsonFile("parametrage/profil.json", new { note = "Aucun profil de tenant paramétré." }));
            return files;
        }

        files.Add(JsonFile("parametrage/profil.json", await _settingsQueries.GetTenantProfile(company, cancellationToken)));
        files.Add(JsonFile("parametrage/fiscal.json", await _settingsQueries.GetFiscalSettings(company, cancellationToken)));
        files.Add(JsonFile("parametrage/comptes-pa.json", await _settingsQueries.GetPaAccounts(company, cancellationToken)));
        files.Add(JsonFile("parametrage/table-tva.json", await _tvaQueries.GetMappingTable(company, cancellationToken)));
        files.Add(JsonFile("parametrage/planification.json", await _settingsQueries.GetExtractionSchedule(company, cancellationToken)));
        files.Add(JsonFile("parametrage/seuils-alerte.json", await _settingsQueries.GetAlertThresholds(company, cancellationToken)));
        return files;
    }

    private async Task<FiscalExportFile> BuildAuditJournalFileAsync(CancellationToken cancellationToken)
    {
        var entries = await _auditQueries.SearchEntries(cancellationToken: cancellationToken);
        return JsonFile("journal/audit.json", new
        {
            cap = AuditJournalCap,
            count = entries.Count,
            note = $"Au plus {AuditJournalCap} entrées les plus récentes (limite de lecture). La piste d'audit fiscale complète est dans le coffre WORM (archive/).",
            entries,
        });
    }

    private void RequireTenant()
    {
        if (!_tenantContext.IsResolved || string.IsNullOrWhiteSpace(_tenantContext.TenantId))
        {
            throw new InvalidOperationException(
                "L'export de réversibilité est tenant-scopé : aucun tenant résolu pour cette opération (blueprint §7).");
        }
    }
}
