namespace Liakont.Agent.Adapters.EncheresV6;

using System;
using System.Collections.Generic;
using Liakont.Agent.Core.Extraction;

/// <summary>
/// Source des PDF de bordereaux EncheresV6 (ADP05), derrière une abstraction pour que la MÊME source
/// (un dossier de fichiers sur le serveur, en V1) serve aussi bien les documents extraits des fixtures
/// que ceux extraits en ODBC réel : la localisation des PDF est INDÉPENDANTE de l'origine des lignes
/// (F01-F02 §4 ; ADR-0004 D2 — capacités déclarées). L'extracteur ne fait que déléguer
/// <see cref="IExtractor.GetAttachments"/> et <see cref="IExtractor.ListPoolDocuments"/> à cette source.
/// <para>
/// LECTURE SEULE STRICTE (CLAUDE.md n°5, F01-F02 R1) : une implémentation ne déplace, ne renomme et
/// ne supprime JAMAIS un fichier source — elle le localise et le pointe (l'agent en transporte une
/// copie). Aucune capacité n'est déclarée tant que sa source n'est pas réellement configurée : la
/// plateforme ne s'appuie que sur ce qui est honoré (jamais de <c>if (source is …)</c>).
/// </para>
/// <para>
/// Deux implémentations : <see cref="FileSystemEncheresV6PdfSource"/> (dossier de fichiers plat, sans
/// accès base) et <see cref="GedTableEncheresV6PdfSource"/> (la GED réelle d'EncheresV6 : fichiers sur
/// disque RÉFÉRENCÉS par les tables GED, liaison <c>GED_Relation</c> — schéma élucidé sur la vraie
/// donnée le 02/07/2026, ce qui lève l'ancienne réserve « stockage GED non documenté »).
/// </para>
/// <para>
/// Le défaut « aucune source PDF configurée » est porté par le null-object
/// <see cref="NullEncheresV6PdfSource"/> (capacités false, listes vides) : un extracteur sans config
/// PDF garde le comportement « pas de PDF » sans branche conditionnelle.
/// </para>
/// </summary>
public interface IEncheresV6PdfSource
{
    /// <summary>La source fournit des PDF LIÉS à un document (capacité <see cref="ExtractorCapabilities.ProvidesSourceDocuments"/>).</summary>
    bool ProvidesSourceDocuments { get; }

    /// <summary>La source fournit un VRAC de PDF non liés (capacité <see cref="ExtractorCapabilities.ProvidesUnlinkedDocumentPool"/>).</summary>
    bool ProvidesUnlinkedDocumentPool { get; }

    /// <summary>
    /// Pièces jointes (PDF) liées à un document, retrouvées par sa <paramref name="sourceReference"/>
    /// (format <c>no_ba=&lt;valeur&gt;</c>, cf. <see cref="EncheresV6RowMapper"/>). Renvoie une liste
    /// VIDE — jamais d'exception — quand la capacité n'est pas déclarée ou qu'aucun PDF n'est trouvé
    /// (un Warning est alors journalisé : capacité déclarée mais document absent — acceptance ADP05).
    /// </summary>
    /// <param name="sourceReference">Référence source du document concerné.</param>
    /// <returns>Les pièces jointes liées (vide si capacité absente ou PDF introuvable).</returns>
    IReadOnlyList<SourceAttachment> GetAttachments(string sourceReference);

    /// <summary>
    /// PDF d'un pool NON lié déposés sur la période [<paramref name="fromInclusiveUtc"/>,
    /// <paramref name="toExclusiveUtc"/>[ (capacité <see cref="ExtractorCapabilities.ProvidesUnlinkedDocumentPool"/>),
    /// par streaming. Vide si la capacité n'est pas déclarée.
    /// </summary>
    /// <param name="fromInclusiveUtc">Borne basse de la période (UTC, incluse).</param>
    /// <param name="toExclusiveUtc">Borne haute de la période (UTC, exclue).</param>
    /// <returns>Les documents de pool de la période (différés/streaming).</returns>
    IEnumerable<PoolDocument> ListPoolDocuments(DateTime fromInclusiveUtc, DateTime toExclusiveUtc);
}
