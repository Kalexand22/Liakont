namespace Liakont.Modules.Archive.Application;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Archive.Contracts;
using Liakont.Modules.Archive.Domain;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Construit le dossier d'export contrôle fiscal (TRK06, F06 §7). Réunit, pour un document ou une période :
/// les paquets d'archive (lus dans le coffre), le rapport d'intégrité complet (<see cref="IArchiveVerifier"/>),
/// les preuves d'ancrage, la chronologie des <c>DocumentEvent</c> (lue via <c>Documents.Contracts</c>) et une
/// notice de vérification en français. Tenant-scopé : coffre et base routés vers le tenant courant.
/// </summary>
public sealed class FiscalControlExportService : IFiscalControlExportService
{
    private const string VerificationNotice =
        """
        NOTICE DE VÉRIFICATION — DOSSIER D'ARCHIVE FISCALE LIAKONT
        ==========================================================

        Ce dossier constitue la piste d'audit d'un document (ou d'une période) transmis à
        l'administration via une Plateforme Agréée. Il est conçu pour rester vérifiable dix ans
        (art. L.123-22 du Code de commerce) et lisible sans le logiciel d'origine (art. 289 V du CGI).

        CONTENU DU DOSSIER
        ------------------
        - <année>/<mois>/<numéro>/payload.json     : le flux EXACT transmis à la Plateforme Agréée.
        - <année>/<mois>/<numéro>/reponse-pa.json  : la réponse de la Plateforme + identifiants DGFiP.
        - <année>/<mois>/<numéro>/document-lisible.html : rendu lisible autonome de la facture.
        - <année>/<mois>/<numéro>/manifest.json    : empreintes SHA-256 de chaque pièce + chaînage.
        - <année>/<mois>/<numéro>/manifest-addendum-*.json : pièces ajoutées a posteriori (chaînées).
        - <année>/<mois>/<numéro>/chronologie.json|.txt : journal horodaté append-only du document.
        - <année>/<mois>/<numéro>/liens-reporting-pieces.json : pour une déclaration d'e-reporting B2C
          (flux 10.3), les pièces source liées de façon immuable à la transmission (traçabilité B2C03).
        - _anchors/...                              : preuves d'ancrage temporel (jetons RFC 3161).
        - rapport-integrite.json                    : résultat de la vérification au moment de l'export.

        COMMENT VÉRIFIER L'INTÉGRITÉ
        ---------------------------
        1. Empreinte des pièces : pour chaque fichier listé dans un manifest, recalculez son
           empreinte SHA-256 et comparez-la à la valeur du manifest. Toute différence = pièce altérée.
        2. Chaînage : chaque entrée porte un chain_hash = SHA256(chain_hash_précédent + package_hash).
           Recalculez la chaîne dans l'ordre ; une rupture localise la première entrée altérée.
        3. Ancrage temporel : les jetons RFC 3161 (_anchors/*.tsr) attestent qu'à une date donnée la
           tête de chaîne portait son empreinte. Ils se vérifient avec tout outil RFC 3161 standard
           (p. ex. « openssl ts -verify ») contre le certificat de l'autorité d'horodatage (TSA) qualifiée.

        LIMITE ASSUMÉE
        --------------
        Ce coffre n'est PAS un système d'archivage électronique certifié NF Z42-013 / NF 461.
        L'intégrité repose sur la chaîne de hashes SHA-256, le scellement qualifié eIDAS (ancrage
        RFC 3161) et, en option, un ancrage blockchain. La certification NF Z42-013 n'est jamais revendiquée.
        """;

    private static readonly JsonSerializerOptions ExportJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IArchiveStore _store;
    private readonly IArchiveEntryStore _entryStore;
    private readonly IArchiveVerifier _verifier;
    private readonly IDocumentQueries _documentQueries;
    private readonly IReportingPieceLinkStore _linkStore;
    private readonly ITenantSettingsQueries _settingsQueries;
    private readonly ITenantContext _tenantContext;

    public FiscalControlExportService(
        IArchiveStore store,
        IArchiveEntryStore entryStore,
        IArchiveVerifier verifier,
        IDocumentQueries documentQueries,
        IReportingPieceLinkStore linkStore,
        ITenantSettingsQueries settingsQueries,
        ITenantContext tenantContext)
    {
        _store = store;
        _entryStore = entryStore;
        _verifier = verifier;
        _documentQueries = documentQueries;
        _linkStore = linkStore;
        _settingsQueries = settingsQueries;
        _tenantContext = tenantContext;
    }

    public async Task<FiscalControlExport> BuildForDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        List<ArchiveEntryRecord> entries = await SelectDocumentEntriesAsync(documentId, cancellationToken);
        return await MaterializeAsync($"document:{documentId}", entries, cancellationToken);
    }

    public async Task<FiscalControlExport> BuildForPeriodAsync(int year, int? month, CancellationToken cancellationToken = default)
    {
        if (month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month), month, "Le mois doit être compris entre 1 et 12, ou nul pour toute l'année.");
        }

        string prefix = month is { } m
            ? $"{year.ToString("D4", CultureInfo.InvariantCulture)}/{m.ToString("D2", CultureInfo.InvariantCulture)}/"
            : $"{year.ToString("D4", CultureInfo.InvariantCulture)}/";

        IReadOnlyList<ArchiveEntryRecord> chain = await _entryStore.GetChainAsync(cancellationToken);
        List<ArchiveEntryRecord> entries = chain
            .Where(e => e.PackagePath.StartsWith(prefix, StringComparison.Ordinal))
            .ToList();

        string scope = month is { } mm
            ? $"période:{year.ToString("D4", CultureInfo.InvariantCulture)}-{mm.ToString("D2", CultureInfo.InvariantCulture)}"
            : $"période:{year.ToString("D4", CultureInfo.InvariantCulture)}";
        return await MaterializeAsync(scope, entries, cancellationToken);
    }

    public async Task<FiscalControlExport> BuildForRangeAsync(DateOnly? fromInclusive, DateOnly? toInclusive, CancellationToken cancellationToken = default)
    {
        List<ArchiveEntryRecord> entries = await SelectRangeEntriesAsync(fromInclusive, toInclusive, cancellationToken);
        string scope = RangeScope(fromInclusive, toInclusive);
        return await MaterializeAsync(scope, entries, cancellationToken);
    }

    public async IAsyncEnumerable<FiscalExportFile> StreamForDocumentAsync(Guid documentId, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        List<ArchiveEntryRecord> entries = await SelectDocumentEntriesAsync(documentId, cancellationToken);
        ArchiveVerificationReport verification = await _verifier.VerifyTenantVaultAsync(cancellationToken);
        await foreach (FiscalExportFile file in StreamFilesAsync(entries, verification, cancellationToken))
        {
            yield return file;
        }
    }

    public async IAsyncEnumerable<FiscalExportFile> StreamForRangeAsync(DateOnly? fromInclusive, DateOnly? toInclusive, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        List<ArchiveEntryRecord> entries = await SelectRangeEntriesAsync(fromInclusive, toInclusive, cancellationToken);
        ArchiveVerificationReport verification = await _verifier.VerifyTenantVaultAsync(cancellationToken);
        await foreach (FiscalExportFile file in StreamFilesAsync(entries, verification, cancellationToken))
        {
            yield return file;
        }
    }

    private static int PeriodKey(int year, int month) => (year * 12) + (month - 1);

    private static string RangeScope(DateOnly? fromInclusive, DateOnly? toInclusive) =>
        $"plage:{(fromInclusive is { } a ? a.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "—")}..{(toInclusive is { } b ? b.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "—")}";

    /// <summary>
    /// Extrait la clé de période (année*12+mois) du chemin de coffre <c>&lt;année&gt;/&lt;mois&gt;/...</c>.
    /// Retourne <c>false</c> si le chemin ne porte pas un préfixe année/mois exploitable (le paquet est alors
    /// hors de toute plage bornée — jamais inclus par erreur).
    /// </summary>
    private static bool TryPeriodKey(string packagePath, out int key)
    {
        key = 0;
        string[] segments = packagePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2
            || !int.TryParse(segments[0], NumberStyles.None, CultureInfo.InvariantCulture, out int year)
            || !int.TryParse(segments[1], NumberStyles.None, CultureInfo.InvariantCulture, out int month)
            || month is < 1 or > 12)
        {
            return false;
        }

        key = PeriodKey(year, month);
        return true;
    }

    private static List<string> ManifestFileNames(byte[] manifestBytes)
    {
        try
        {
            using var document = JsonDocument.Parse(manifestBytes);
            if (!document.RootElement.TryGetProperty("files", out JsonElement files) || files.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var names = new List<string>();
            foreach (JsonElement file in files.EnumerateArray())
            {
                if (file.TryGetProperty("name", out JsonElement name) && name.GetString() is { } value)
                {
                    names.Add(value);
                }
            }

            return names;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string RenderChronologyText(Guid documentId, DocumentDto? document, IReadOnlyList<DocumentEventDto> events)
    {
        var builder = new StringBuilder();
        if (document is not null)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"=== Chronologie du document {document.DocumentNumber} ({document.DocumentType}, émis le {document.IssueDate:yyyy-MM-dd}) ===");
            builder.AppendLine(CultureInfo.InvariantCulture, $"État : {document.State} | Total TTC : {document.TotalGross.ToString(CultureInfo.InvariantCulture)} | SIREN fournisseur : {document.SupplierSiren ?? "—"}");
            builder.AppendLine(CultureInfo.InvariantCulture, $"Identifiant : {document.Id}");
        }
        else
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"=== Chronologie du document {documentId} (en-tête indisponible) ===");
        }

        builder.AppendLine();
        foreach (DocumentEventDto evt in events)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"[{evt.TimestampUtc:O}] {evt.EventType} — {evt.Detail ?? string.Empty}");
            if (!string.IsNullOrEmpty(evt.OperatorIdentity))
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"    (opérateur : {evt.OperatorIdentity})");
            }
        }

        return builder.ToString();
    }

    private static string DirectoryOf(string relativePath)
    {
        int lastSlash = relativePath.LastIndexOf('/');
        return lastSlash >= 0 ? relativePath[..(lastSlash + 1)] : string.Empty;
    }

    private static string ToManifestPath(string proofPath)
    {
        int lastDot = proofPath.LastIndexOf('.');
        return lastDot >= 0 ? proofPath[..lastDot] + ".json" : proofPath + ".json";
    }

    private static string GuessContentType(string fileName) =>
        fileName.ToLowerInvariant() switch
        {
            var n when n.EndsWith(".json", StringComparison.Ordinal) => "application/json",
            var n when n.EndsWith(".html", StringComparison.Ordinal) => "text/html; charset=utf-8",
            var n when n.EndsWith(".xml", StringComparison.Ordinal) => "application/xml",
            var n when n.EndsWith(".pdf", StringComparison.Ordinal) => "application/pdf",
            _ => "application/octet-stream",
        };

    private async Task<List<ArchiveEntryRecord>> SelectDocumentEntriesAsync(Guid documentId, CancellationToken cancellationToken)
    {
        IReadOnlyList<ArchiveEntryRecord> chain = await _entryStore.GetChainAsync(cancellationToken);
        return chain.Where(e => e.DocumentId == documentId).ToList();
    }

    private async Task<List<ArchiveEntryRecord>> SelectRangeEntriesAsync(DateOnly? fromInclusive, DateOnly? toInclusive, CancellationToken cancellationToken)
    {
        if (fromInclusive is { } f && toInclusive is { } t && t < f)
        {
            throw new ArgumentException($"La borne haute ({t:yyyy-MM-dd}) précède la borne basse ({f:yyyy-MM-dd}).", nameof(toInclusive));
        }

        IReadOnlyList<ArchiveEntryRecord> chain = await _entryStore.GetChainAsync(cancellationToken);

        if (fromInclusive is null && toInclusive is null)
        {
            // Tout le coffre du tenant (export de réversibilité).
            return [.. chain];
        }

        int fromKey = fromInclusive is { } lo ? PeriodKey(lo.Year, lo.Month) : int.MinValue;
        int toKey = toInclusive is { } hi ? PeriodKey(hi.Year, hi.Month) : int.MaxValue;
        return chain
            .Where(e => TryPeriodKey(e.PackagePath, out int key) && key >= fromKey && key <= toKey)
            .ToList();
    }

    /// <summary>
    /// Variante MATÉRIALISÉE : collecte le flux paresseux dans une liste ordonnée et renseigne les méta
    /// (vérification, complétude, notice). Pour petits dossiers / tests ; les gros volumes passent par les
    /// variantes <c>Stream…</c> (l'endpoint API03 écrit le ZIP au fil de l'eau).
    /// </summary>
    private async Task<FiscalControlExport> MaterializeAsync(
        string scope,
        IReadOnlyList<ArchiveEntryRecord> entries,
        CancellationToken cancellationToken)
    {
        ArchiveVerificationReport verification = await _verifier.VerifyTenantVaultAsync(cancellationToken);

        var files = new List<FiscalExportFile>();
        await foreach (FiscalExportFile file in StreamFilesAsync(entries, verification, cancellationToken))
        {
            files.Add(file);
        }

        List<FiscalExportFile> ordered = files
            .OrderBy(f => f.Path, StringComparer.Ordinal)
            .ToList();

        bool isComplete = entries.Select(e => e.DocumentId).Distinct().Any();
        return new FiscalControlExport(scope, ordered, verification, isComplete, VerificationNotice);
    }

    /// <summary>
    /// Cœur PARESSEUX de l'assemblage : produit chaque fichier du dossier l'un après l'autre, en lisant
    /// chaque pièce du coffre juste avant de la rendre (une seule pièce en mémoire à la fois). L'ordre :
    /// paquets + pièces, chronologies, preuves d'ancrage, rapport d'intégrité, notice. Un <c>seen</c> de
    /// CHEMINS (léger) évite les doublons d'entrée ZIP.
    /// </summary>
    private async IAsyncEnumerable<FiscalExportFile> StreamFilesAsync(
        IReadOnlyList<ArchiveEntryRecord> entries,
        ArchiveVerificationReport verification,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string tenant = RequireTenant();

        // Société du tenant SÉLECTIONNÉ (résolue depuis sa base, comme l'export de réversibilité) : correcte
        // y compris pour un export OPÉRATEUR cross-tenant — contrairement au filtre acteur, nul dans ce cas.
        // null = aucun profil société (tenant non provisionné) → aucun lien reporting↔pièces ajouté.
        Guid? reportingCompanyId = await _settingsQueries.GetCurrentCompanyId(cancellationToken);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var documentIds = new List<Guid>();
        var documentDir = new Dictionary<Guid, string>();

        // 1. Paquets d'archive (paquet + addenda) lus dans le coffre, pièce par pièce.
        foreach (ArchiveEntryRecord entry in entries)
        {
            if (documentDir.TryAdd(entry.DocumentId, DirectoryOf(entry.PackagePath)))
            {
                documentIds.Add(entry.DocumentId);
            }

            byte[]? manifestBytes = await TryReadAsync(tenant, entry.PackagePath, cancellationToken);
            if (manifestBytes is null)
            {
                // Manifest manquant : l'absence est signalée par le rapport d'intégrité, on ne fabrique rien.
                continue;
            }

            if (seen.Add(entry.PackagePath))
            {
                yield return new FiscalExportFile(entry.PackagePath, "application/json", manifestBytes);
            }

            string directory = DirectoryOf(entry.PackagePath);
            foreach (string name in ManifestFileNames(manifestBytes))
            {
                string path = directory + name;
                if (!seen.Add(path))
                {
                    continue;
                }

                byte[]? content = await TryReadAsync(tenant, path, cancellationToken);
                if (content is not null)
                {
                    yield return new FiscalExportFile(path, GuessContentType(name), content);
                }
            }
        }

        // 2. Chronologie (DocumentEvents) + traçabilité reporting↔pièces (B2C03) par document, dans son
        //    répertoire de paquet.
        foreach (Guid documentId in documentIds)
        {
            foreach (FiscalExportFile file in await BuildChronologyFilesAsync(documentId, documentDir[documentId], cancellationToken))
            {
                if (seen.Add(file.Path))
                {
                    yield return file;
                }
            }

            if (reportingCompanyId is { } companyId)
            {
                FiscalExportFile? linkFile = await BuildReportingLinkFileAsync(companyId, documentId, documentDir[documentId], cancellationToken);
                if (linkFile is { } file2 && seen.Add(file2.Path))
                {
                    yield return file2;
                }
            }
        }

        // 3. Preuves d'ancrage (jetons + manifests) référencées par le rapport.
        foreach (ArchiveAnchorVerification anchor in verification.Anchors)
        {
            if (string.IsNullOrEmpty(anchor.ProofPath))
            {
                continue;
            }

            if (seen.Add(anchor.ProofPath))
            {
                byte[]? proof = await TryReadAsync(tenant, anchor.ProofPath, cancellationToken);
                if (proof is not null)
                {
                    yield return new FiscalExportFile(anchor.ProofPath, "application/timestamp-token", proof);
                }
            }

            string manifestPath = ToManifestPath(anchor.ProofPath);
            if (seen.Add(manifestPath))
            {
                byte[]? manifest = await TryReadAsync(tenant, manifestPath, cancellationToken);
                if (manifest is not null)
                {
                    yield return new FiscalExportFile(manifestPath, "application/json", manifest);
                }
            }
        }

        // 4. Rapport d'intégrité + notice.
        if (seen.Add("rapport-integrite.json"))
        {
            yield return new FiscalExportFile(
                "rapport-integrite.json",
                "application/json",
                JsonSerializer.SerializeToUtf8Bytes(verification, ExportJsonOptions));
        }

        if (seen.Add("notice-verification.txt"))
        {
            yield return new FiscalExportFile(
                "notice-verification.txt",
                "text/plain; charset=utf-8",
                Encoding.UTF8.GetBytes(VerificationNotice));
        }
    }

    private async Task<IReadOnlyList<FiscalExportFile>> BuildChronologyFilesAsync(
        Guid documentId,
        string packageDir,
        CancellationToken cancellationToken)
    {
        DocumentDto? document = await _documentQueries.GetByIdAsync(documentId, cancellationToken);
        IReadOnlyList<DocumentEventDto> events = await _documentQueries.GetEventsAsync(documentId, cancellationToken);

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(new { document, events }, ExportJsonOptions);
        byte[] text = Encoding.UTF8.GetBytes(RenderChronologyText(documentId, document, events));

        return
        [
            new FiscalExportFile(packageDir + "chronologie.json", "application/json", json),
            new FiscalExportFile(packageDir + "chronologie.txt", "text/plain; charset=utf-8", text),
        ];
    }

    /// <summary>
    /// Traçabilité reporting↔pièces (B2C03) : pour une transmission d'e-reporting B2C (déclaration 10.3),
    /// ajoute au dossier la liste IMMUABLE de ses pièces source liées (sens transmission → pièces), gelées à
    /// la transmission. Un document ORDINAIRE (facture, avoir, B2B — aucun lien) ne produit AUCUN fichier :
    /// l'export des documents non-10.3 est inchangé. Le lien ne porte aucun secret PA (identifiant de
    /// document + référence de pièce + horodatage) — rien à masquer.
    /// </summary>
    private async Task<FiscalExportFile?> BuildReportingLinkFileAsync(
        Guid companyId,
        Guid documentId,
        string packageDir,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ReportingPieceLink> links = await _linkStore.GetByDocumentAsync(companyId, documentId, cancellationToken);
        if (links.Count == 0)
        {
            return null;
        }

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(
            new { documentId, links },
            ExportJsonOptions);
        return new FiscalExportFile(packageDir + "liens-reporting-pieces.json", "application/json", json);
    }

    private async Task<byte[]?> TryReadAsync(string tenant, string path, CancellationToken cancellationToken)
    {
        try
        {
            return await _store.ReadAsync(tenant, path, cancellationToken);
        }
        catch (ArchiveObjectNotFoundException)
        {
            // Pièce absente : signalée par le rapport d'intégrité, jamais inventée.
            return null;
        }
    }

    private string RequireTenant()
    {
        if (!_tenantContext.IsResolved || string.IsNullOrWhiteSpace(_tenantContext.TenantId))
        {
            throw new InvalidOperationException(
                "L'export contrôle fiscal est tenant-scopé : aucun tenant résolu pour cette opération (blueprint §7).");
        }

        return ArchivePackageLayout.SanitizeSegment(_tenantContext.TenantId);
    }
}
