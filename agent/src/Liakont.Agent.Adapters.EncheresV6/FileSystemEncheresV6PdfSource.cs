namespace Liakont.Agent.Adapters.EncheresV6;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Liakont.Agent.Core.Extraction;
using Liakont.Agent.Core.Logging;

/// <summary>
/// Source PDF d'EncheresV6 adossée au SYSTÈME DE FICHIERS (ADP05) : les bordereaux PDF sont des fichiers
/// déposés dans un (ou deux) dossier(s) du serveur. Implémente les deux modes de
/// <see cref="IEncheresV6PdfSource"/> selon <see cref="EncheresV6PdfSourceOptions"/> :
/// <list type="bullet">
///   <item><b>Lié</b> : <see cref="GetAttachments"/> retrouve le(s) PDF d'un bordereau par le <c>no_ba</c>
///   extrait de sa référence source (le nom de fichier doit contenir le <c>no_ba</c> comme jeton délimité).</item>
///   <item><b>Pool</b> : <see cref="ListPoolDocuments"/> expose les PDF du dossier déposés sur la période
///   (par date de dernière écriture), sans lien — la réconciliation est plateforme (TRK07).</item>
/// </list>
/// <para>
/// LECTURE SEULE STRICTE (CLAUDE.md n°5, F01-F02 R1) : on n'ÉNUMÈRE et ne LIT que des métadonnées de
/// fichiers (nom, date d'écriture) ; aucun fichier n'est jamais déplacé, renommé, supprimé ni écrit.
/// </para>
/// <para>
/// JAMAIS D'ÉCHEC DU RUN (acceptance ADP05) : un dossier absent, une erreur d'E/S ou un PDF introuvable
/// ne lèvent jamais d'exception — ils produisent une liste vide et un Warning opérateur en français
/// (CLAUDE.md n°12). Un PDF introuvable pour un document dont la capacité « lié » est déclarée est
/// précisément le cas « capacité déclarée mais document absent → liste vide + Warning ».
/// </para>
/// </summary>
public sealed class FileSystemEncheresV6PdfSource : IEncheresV6PdfSource
{
    private readonly EncheresV6PdfSourceOptions _options;
    private readonly IAgentLog _log;

    /// <summary>Crée une source PDF « dossier de fichiers ».</summary>
    /// <param name="options">Configuration (dossiers lié/pool, motif de recherche).</param>
    /// <param name="log">Journal de l'agent (Warnings sur PDF/dossier introuvable). Best-effort, ne lève jamais.</param>
    /// <exception cref="ArgumentNullException">Si <paramref name="options"/> ou <paramref name="log"/> est nul.</exception>
    public FileSystemEncheresV6PdfSource(EncheresV6PdfSourceOptions options, IAgentLog log)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <inheritdoc />
    public bool ProvidesSourceDocuments => _options.LinkedModeEnabled;

    /// <inheritdoc />
    public bool ProvidesUnlinkedDocumentPool => _options.PoolModeEnabled;

    /// <inheritdoc />
    public IReadOnlyList<SourceAttachment> GetAttachments(string sourceReference)
    {
        if (!_options.LinkedModeEnabled)
        {
            // Capacité non déclarée : vide, sans Warning (comportement normal d'une source sans mode lié).
            return Array.Empty<SourceAttachment>();
        }

        string? noBa = ExtractNoBa(sourceReference);
        if (noBa is null)
        {
            _log.Warn("PDF lié EncheresV6 : référence source vide ou inexploitable — aucun PDF recherché.");
            return Array.Empty<SourceAttachment>();
        }

        string folder = _options.LinkedFolderPath!;
        List<string>? files = EnumeratePdfFiles(folder, "PDF lié");
        if (files is null)
        {
            // Dossier absent ou erreur d'E/S déjà journalisée : jamais d'échec du run.
            return Array.Empty<SourceAttachment>();
        }

        var matches = files
            .Where(path => ContainsDelimitedToken(Path.GetFileNameWithoutExtension(path), noBa))
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
            .Select(path => new SourceAttachment(sourceReference, path))
            .ToList();

        if (matches.Count == 0)
        {
            // Capacité déclarée mais document absent : liste vide + Warning, jamais d'échec (acceptance ADP05).
            _log.Warn(
                "PDF lié EncheresV6 introuvable pour le bordereau « no_ba=" + noBa + " » dans « " + folder
                + " » : aucun fichier dont le nom contient ce numéro. Bordereau transmis sans pièce jointe.");
        }

        return matches;
    }

    /// <inheritdoc />
    public IEnumerable<PoolDocument> ListPoolDocuments(DateTime fromInclusiveUtc, DateTime toExclusiveUtc)
    {
        if (!_options.PoolModeEnabled)
        {
            return Array.Empty<PoolDocument>();
        }

        // Matérialisé (pas un iterator paresseux) pour que toute erreur d'E/S de listage soit captée et
        // journalisée ICI (jamais propagée hors du run), tout en restant O(nombre de fichiers du pool).
        return BuildPoolDocuments(toExclusiveUtc);
    }

    /// <summary>
    /// Extrait le <c>no_ba</c> d'une référence source de document (<c>"no_ba=&lt;valeur&gt;"</c>, format
    /// produit par <see cref="EncheresV6RowMapper"/>). Renvoie <c>null</c> si la référence est vide ou ne
    /// porte pas de valeur exploitable.
    /// </summary>
    private static string? ExtractNoBa(string? sourceReference)
    {
        if (string.IsNullOrWhiteSpace(sourceReference))
        {
            return null;
        }

        string trimmed = sourceReference!.Trim();
        string value = trimmed.StartsWith(EncheresV6RowMapper.SourceReferencePrefix, StringComparison.Ordinal)
            ? trimmed.Substring(EncheresV6RowMapper.SourceReferencePrefix.Length)
            : trimmed;

        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// Indique si <paramref name="stem"/> contient <paramref name="token"/> comme JETON DÉLIMITÉ
    /// (insensible à la casse) : le <c>no_ba</c> doit apparaître entouré de bornes non alphanumériques
    /// (début/fin de nom, séparateur). Évite les faux rapprochements « 4500 » ⊂ « 45000 » / « 14500 »,
    /// tout en acceptant « 4500.pdf », « bordereau-4500.pdf », « F-2026_4500_v2.pdf ».
    /// </summary>
    private static bool ContainsDelimitedToken(string stem, string token)
    {
        if (string.IsNullOrEmpty(stem) || string.IsNullOrEmpty(token))
        {
            return false;
        }

        int start = 0;
        while (true)
        {
            int idx = stem.IndexOf(token, start, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                return false;
            }

            bool leftOk = idx == 0 || !IsTokenChar(stem[idx - 1]);
            int after = idx + token.Length;
            bool rightOk = after >= stem.Length || !IsTokenChar(stem[after]);
            if (leftOk && rightOk)
            {
                return true;
            }

            start = idx + 1;
        }
    }

    private static bool IsTokenChar(char c) => char.IsLetterOrDigit(c);

    private List<PoolDocument> BuildPoolDocuments(DateTime toExclusiveUtc)
    {
        string folder = _options.PoolFolderPath!;
        List<string>? files = EnumeratePdfFiles(folder, "PDF pool");
        if (files is null)
        {
            return new List<PoolDocument>();
        }

        var documents = new List<PoolDocument>();
        foreach (string path in files.OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal))
        {
            DateTime? lastWriteUtc = TryGetLastWriteTimeUtc(path);
            if (lastWriteUtc is null)
            {
                // Métadonnée illisible (fichier disparu/verrouillé entre le listage et la lecture) : on ignore
                // ce fichier (déjà journalisé), jamais d'échec du run.
                continue;
            }

            // PAS DE BORNE BASSE sur la période : la date de dernière écriture (mtime) est PRÉSERVÉE par une
            // copie de fichier (Explorer / copy / robocopy), donc un PDF ancien déposé TARDIVEMENT dans le pool
            // garderait un mtime antérieur au filigrane et deviendrait DÉFINITIVEMENT INVISIBLE — exactement le
            // piège que le contrat IExtractor interdit (« aucun document définitivement invisible une fois le
            // filigrane avancé »). On ne filtre donc que la borne haute (< to, anti-futur) ; l'idempotence est
            // garantie en AVAL par l'anti-re-push (par PoolReference, ExtractionCycle.CollectPoolPdfs) : chaque
            // PDF du pool n'est poussé qu'une seule fois, sans jamais en perdre un.
            if (lastWriteUtc.Value < toExclusiveUtc)
            {
                // PoolReference = nom de fichier : clé STABLE d'idempotence de file (PoolDocument), inchangée
                // d'un run à l'autre tant que le fichier n'est pas renommé.
                documents.Add(new PoolDocument(Path.GetFileName(path), path));
            }
        }

        return documents;
    }

    /// <summary>
    /// Liste les fichiers PDF d'un dossier (motif de la config, niveau supérieur uniquement) en LECTURE
    /// SEULE. Renvoie <c>null</c> (et journalise un Warning) si le dossier est absent, le motif de recherche
    /// invalide (<see cref="ArgumentException"/>) ou une erreur d'E/S survient — jamais d'exception
    /// (acceptance ADP05 : jamais d'échec du run, même sur un paramétrage erroné).
    /// </summary>
    private List<string>? EnumeratePdfFiles(string folder, string contextLabel)
    {
        if (!Directory.Exists(folder))
        {
            _log.Warn(
                contextLabel + " EncheresV6 : dossier configuré introuvable « " + folder
                + " ». Vérifiez le chemin dans la configuration de l'adaptateur. Aucun PDF transmis.");
            return null;
        }

        try
        {
            return Directory.EnumerateFiles(folder, _options.SearchPattern, SearchOption.TopDirectoryOnly).ToList();
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
        {
            // ArgumentException : motif de recherche invalide (caractères interdits) — paramétrage erroné, à
            // corriger dans la config ; on dégrade en Warning + liste vide, jamais en échec du run (ADP05).
            _log.Warn(
                contextLabel + " EncheresV6 : lecture du dossier « " + folder + " » impossible (" + ex.Message
                + "). Vérifiez le dossier et le motif de recherche dans la configuration. Aucun PDF transmis pour ce cycle.");
            return null;
        }
    }

    private DateTime? TryGetLastWriteTimeUtc(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            _log.Warn(
                "PDF pool EncheresV6 : date du fichier « " + path + " » illisible (" + ex.Message
                + ") — fichier ignoré pour ce cycle.");
            return null;
        }
    }
}
