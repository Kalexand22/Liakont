namespace Liakont.Agent.Adapters.EncheresV6;

using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using Liakont.Agent.Adapters.EncheresV6.Source;
using Liakont.Agent.Core.Extraction;
using Liakont.Agent.Core.Logging;

/// <summary>
/// Source PDF d'EncheresV6 adossée aux TABLES GED de la base source (lecture ODBC seule) : la GED
/// d'EncheresV6 stocke les bordereaux PDF en FICHIERS sur disque et référence chaque fichier en base —
/// <c>GED_Relation</c> lie le n° de bordereau (<c>Ref_numerique1</c>, flux 5 = BA / 6 = BV, table
/// <c>GED_Type</c>) au document GED, <c>GED_document_joint</c> porte les composantes du chemin, et
/// <c>GED_Param_Document</c> la racine de stockage. Le chemin est reconstruit à l'identique de
/// l'arborescence produite par EncheresV6 (vérifiée sur la donnée réelle) :
/// <c>&lt;racine&gt;\&lt;No_dossier&gt;\&lt;Année&gt;\&lt;Mois sur 2 chiffres&gt;\&lt;Type_modele&gt;\&lt;Référence&gt;\&lt;Nom_fichier&gt;&lt;Extension&gt;</c>.
/// <para>
/// La RACINE vient de <c>GED_Param_Document.Chemin_stockage</c> (le partage du serveur client — valide
/// quand l'agent tourne sur site), avec OVERRIDE possible par configuration
/// (<c>adapterConfig.EncheresV6.gedPdfRoot</c>) quand les fichiers sont montés ailleurs (démo, réplication).
/// </para>
/// <para>
/// LECTURE SEULE STRICTE (CLAUDE.md n°5) : requêtes <c>SELECT</c> gardées + lecture de fichiers ; aucun
/// fichier n'est jamais déplacé, renommé, supprimé ni écrit.
/// </para>
/// <para>
/// ROBUSTESSE — deux régimes d'échec DISTINCTS, contrairement à <see cref="FileSystemEncheresV6PdfSource"/> :
/// une indisponibilité ODBC (connexion/requête) PROPAGE <see cref="SourceUnavailableException"/> — le cycle
/// avorte, le filigrane n'avance pas, le bordereau ET son PDF restent ré-extractibles (réessayable, comme
/// l'extraction elle-même) ; avaler l'erreur transformerait un incident passager en PDF silencieusement
/// perdu. Un problème de DONNÉE (référence inexploitable, type de stockage inconnu, racine absente, fichier
/// introuvable, chemin hors racine) produit un Warning opérateur français + le bordereau part sans pièce
/// jointe — jamais d'échec du run pour une donnée isolée.
/// </para>
/// </summary>
public sealed class GedTableEncheresV6PdfSource : IEncheresV6PdfSource
{
    private const int QueryTimeoutSeconds = 30;

    private const string SourceUnavailableMessage =
        "La GED EncheresV6 est momentanément indisponible (connexion ou requête ODBC sur les tables GED). "
        + "Vérifiez que la base et le pilote ODBC sont accessibles ; le prochain cycle d'extraction réessaiera automatiquement.";

    private readonly ISourceConnectionFactory _connectionFactory;
    private readonly EncheresV6Schema _schema;
    private readonly string _dossier;
    private readonly string? _storageRootOverride;
    private readonly IAgentLog _log;

    /// <summary>Crée la source PDF « tables GED ».</summary>
    /// <param name="connectionFactory">Fabrique de connexions ODBC (lecture seule) — la MÊME que l'extracteur.</param>
    /// <param name="schema">Connaissance du schéma (préfixe de tables paramétrable).</param>
    /// <param name="dossier">N° de dossier comptable (filtre tenant : un agent ne sert que SON dossier).</param>
    /// <param name="storageRootOverride">
    /// Racine de stockage des fichiers GED remplaçant <c>GED_Param_Document.Chemin_stockage</c>
    /// (<c>null</c>/vide = racine lue en base). Paramétrage de déploiement, jamais une donnée du code.
    /// </param>
    /// <param name="log">Journal de l'agent (Warnings opérateur). Best-effort, ne lève jamais.</param>
    internal GedTableEncheresV6PdfSource(
        ISourceConnectionFactory connectionFactory,
        EncheresV6Schema schema,
        string dossier,
        string? storageRootOverride,
        IAgentLog log)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        if (string.IsNullOrWhiteSpace(dossier))
        {
            throw new ArgumentException("Le n° de dossier (filtre tenant) est requis.", nameof(dossier));
        }

        _dossier = dossier.Trim();
        _storageRootOverride = string.IsNullOrWhiteSpace(storageRootOverride) ? null : storageRootOverride!.Trim();
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <inheritdoc />
    public bool ProvidesSourceDocuments => true;

    /// <inheritdoc />
    public bool ProvidesUnlinkedDocumentPool => false;

    /// <inheritdoc />
    public IReadOnlyList<SourceAttachment> GetAttachments(string sourceReference)
    {
        if (!TryResolveGedFlux(sourceReference, out int codeFlux, out string documentLabel, out string reference))
        {
            // Document sans liaison GED SOURCÉE (facture client, note d'honoraires… : Ref_numerique1
            // vaut 0 sur ces flux dans la donnée réelle — liaison non élucidée, jamais devinée). Vide,
            // sans Warning : c'est le comportement normal de ces documents.
            return Array.Empty<SourceAttachment>();
        }

        if (!int.TryParse(reference, NumberStyles.Integer, CultureInfo.InvariantCulture, out int refNumerique))
        {
            _log.Warn(
                "PDF GED EncheresV6 : la référence du " + documentLabel + " « " + reference + " » n'est pas "
                + "un numéro exploitable pour la liaison GED (Ref_numerique1). Document transmis sans pièce jointe.");
            return Array.Empty<SourceAttachment>();
        }

        var attachments = new List<SourceAttachment>();
        using (IDbConnection connection = SourceQuery.Open(_connectionFactory, SourceUnavailableMessage))
        using (IDbCommand command = SourceQuery.CreateSelect(connection, _schema.SelectGedLinkedPdfSql, QueryTimeoutSeconds, codeFlux, refNumerique, _dossier))
        using (IDataReader reader = SourceQuery.ExecuteReader(command, SourceUnavailableMessage))
        {
            while (SourceQuery.Read(reader, SourceUnavailableMessage))
            {
                SourceAttachment? attachment = TryBuildAttachment(reader, sourceReference, documentLabel, reference);
                if (attachment != null)
                {
                    attachments.Add(attachment);
                }
            }
        }

        if (attachments.Count == 0)
        {
            // Capacité déclarée mais document absent : liste vide + Warning, jamais d'échec (acceptance
            // ADP05). Cas réel et légitime : tous les bordereaux n'ont pas de PDF en GED.
            _log.Warn(
                "PDF GED EncheresV6 introuvable pour le " + documentLabel + " « " + reference + " » : aucun "
                + "document GED lié (ou fichier absent, voir Warnings précédents). Document transmis sans pièce jointe.");
        }

        return attachments;
    }

    /// <inheritdoc />
    /// <remarks>La GED lie chaque fichier à son document : il n'existe pas de « vrac » GED à réconcilier.</remarks>
    public IEnumerable<PoolDocument> ListPoolDocuments(DateTime fromInclusiveUtc, DateTime toExclusiveUtc) =>
        Array.Empty<PoolDocument>();

    /// <summary>
    /// Résout le flux GED d'une référence source de document : <c>encheresv6:ba:*</c> → flux BA (5),
    /// <c>encheresv6:bv:*</c> → flux BV (6). Les autres références (factures client, notes d'honoraires,
    /// références vides) ne portent pas de liaison GED sourcée.
    /// </summary>
    private static bool TryResolveGedFlux(string? sourceReference, out int codeFlux, out string documentLabel, out string reference)
    {
        codeFlux = 0;
        documentLabel = string.Empty;
        reference = string.Empty;
        if (string.IsNullOrWhiteSpace(sourceReference))
        {
            return false;
        }

        string trimmed = sourceReference!.Trim();
        if (trimmed.StartsWith(EncheresV6RowMapper.SourceRefBaPrefix, StringComparison.Ordinal))
        {
            codeFlux = EncheresV6Schema.GedFluxBa;
            documentLabel = "bordereau acheteur";
            reference = trimmed.Substring(EncheresV6RowMapper.SourceRefBaPrefix.Length).Trim();
        }
        else if (trimmed.StartsWith(EncheresV6RowMapper.SourceRefBvPrefix, StringComparison.Ordinal))
        {
            codeFlux = EncheresV6Schema.GedFluxBv;
            documentLabel = "bordereau vendeur";
            reference = trimmed.Substring(EncheresV6RowMapper.SourceRefBvPrefix.Length).Trim();
        }
        else
        {
            return false;
        }

        return reference.Length > 0;
    }

    /// <summary>
    /// Reconstruit le chemin du fichier d'une ligne GED et le valide (type de stockage géré, racine
    /// connue, chemin SOUS la racine, fichier présent). Toute anomalie de donnée produit un Warning
    /// opérateur + <c>null</c> (le bordereau part sans cette pièce jointe), jamais une exception.
    /// </summary>
    private SourceAttachment? TryBuildAttachment(IDataReader reader, string sourceReference, string documentLabel, string reference)
    {
        string typeStockage = (OdbcCellReader.GetString(reader, EncheresV6Schema.ColGedTypeStockage) ?? string.Empty).Trim();
        if (!string.Equals(typeStockage, EncheresV6Schema.GedStockageDisque, StringComparison.OrdinalIgnoreCase))
        {
            // Seul le stockage « fichier sur disque » est sourcé (100 % de la donnée réelle observée).
            // Un autre code n'est JAMAIS deviné (CLAUDE.md n°2) : Warning + document sans cette pièce.
            _log.Warn(
                "PDF GED EncheresV6 : type de stockage « " + typeStockage + " » non géré pour le " + documentLabel
                + " « " + reference + " » (seul « D », fichier sur disque, est pris en charge). Pièce jointe ignorée.");
            return null;
        }

        string? root = _storageRootOverride ?? (OdbcCellReader.GetString(reader, EncheresV6Schema.ColGedCheminStockage) ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(root))
        {
            _log.Warn(
                "PDF GED EncheresV6 : aucune racine de stockage pour le " + documentLabel + " « " + reference
                + " » (GED_Param_Document.Chemin_stockage vide et pas d'override « gedPdfRoot » dans la "
                + "configuration de l'adaptateur). Pièce jointe ignorée.");
            return null;
        }

        string fileName =
            (OdbcCellReader.GetString(reader, EncheresV6Schema.ColGedNomFichier) ?? string.Empty).Trim()
            + (OdbcCellReader.GetString(reader, EncheresV6Schema.ColGedExtensionFichier) ?? string.Empty).Trim();
        string typeModele = (OdbcCellReader.GetString(reader, EncheresV6Schema.ColGedTypeModele) ?? string.Empty).Trim();
        string gedReference = (OdbcCellReader.GetString(reader, EncheresV6Schema.ColGedReference) ?? string.Empty).Trim();
        int noDossier = OdbcCellReader.GetInt(reader, EncheresV6Schema.ColGedNoDossier);
        int annee = OdbcCellReader.GetInt(reader, EncheresV6Schema.ColGedAnnee);
        int mois = OdbcCellReader.GetInt(reader, EncheresV6Schema.ColGedMois);

        string fullPath;
        try
        {
            // Arborescence EncheresV6 vérifiée sur la donnée réelle : dossier \ année \ mois (2 chiffres)
            // \ type \ référence \ fichier. GetFullPath + contrôle sous-racine : une composante de base
            // malformée (« .. », chemin absolu) ne doit JAMAIS faire lire un fichier hors de la racine GED.
            string fullRoot = Path.GetFullPath(root);
            string rootWithSeparator = fullRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? fullRoot
                : fullRoot + Path.DirectorySeparatorChar;
            fullPath = Path.GetFullPath(Path.Combine(
                fullRoot,
                noDossier.ToString(CultureInfo.InvariantCulture),
                annee.ToString(CultureInfo.InvariantCulture),
                mois.ToString("00", CultureInfo.InvariantCulture),
                typeModele,
                gedReference,
                fileName));
            if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                _log.Warn(
                    "PDF GED EncheresV6 : le chemin reconstruit pour le " + documentLabel + " « " + reference
                    + " » sort de la racine GED configurée — donnée GED suspecte, pièce jointe ignorée. "
                    + "Vérifiez la ligne GED_document_joint correspondante.");
                return null;
            }
        }
        catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
        {
            _log.Warn(
                "PDF GED EncheresV6 : chemin invalide pour le " + documentLabel + " « " + reference + " » ("
                + ex.Message + "). Pièce jointe ignorée ; vérifiez la ligne GED_document_joint et la racine configurée.");
            return null;
        }

        if (!File.Exists(fullPath))
        {
            _log.Warn(
                "PDF GED EncheresV6 : fichier référencé introuvable pour le " + documentLabel + " « " + reference
                + " » : « " + fullPath + " ». Vérifiez la racine GED (« gedPdfRoot ») et la présence du fichier. "
                + "Document transmis sans cette pièce jointe.");
            return null;
        }

        return new SourceAttachment(sourceReference, fullPath, fileName);
    }
}
