namespace Liakont.Agent.Core;

using System;
using System.Collections.Generic;
using System.IO;
using Liakont.Agent.Contracts.Ged;

/// <summary>
/// Contrat d'un extracteur du canal GED (F19 §4.6) — documents NON-facture (GED dynamique &amp;
/// coffre-fort). SÉPARÉ d'<see cref="IExtractor"/> (le canal fiscal) pour préserver les invariants
/// facture R1–R9 : un adaptateur peut implémenter l'un, l'autre, ou les deux. Comme
/// <see cref="IExtractor"/>, l'extracteur GED vit dans <c>Liakont.Agent.Core</c> (les DTO purs, eux,
/// vivent dans <c>Liakont.Agent.Contracts.Ged</c> — RL-15). RÈGLES du contrat : lecture seule stricte
/// (jamais d'écriture/verrou sur la source, CLAUDE.md n°5) ; idempotence (deux extractions d'une même
/// période renvoient les mêmes <see cref="IngestedDocumentDto.SourceReference"/>) ; streaming ;
/// l'agent EXTRAIT BRUT et DÉCLARE — AUCUNE interprétation, AUCUNE logique métier (mapping, résolution
/// d'identité, DEFER vivent sur la plateforme, CLAUDE.md n°6). La plateforme s'adapte aux
/// <see cref="Capabilities"/> DÉCLARÉES, jamais par <c>if (source is …)</c>.
/// </summary>
public interface IManagedExtractor
{
    /// <summary>Nom de la source extraite (identifiant du plug-in, ex. « EncheresV6 »).</summary>
    string SourceName { get; }

    /// <summary>Capacités GED DÉCLARÉES de la source (F19 §4.6) — la plateforme s'y adapte, jamais par <c>if (source is …)</c>.</summary>
    ManagedExtractorCapabilitiesDto Capabilities { get; }

    /// <summary>
    /// Extrait les documents GÉRÉS (non-facture) d'une période, en LECTURE SEULE et par streaming.
    /// Idempotent (même période → mêmes <see cref="IngestedDocumentDto.SourceReference"/>). L'agent
    /// n'interprète rien : il émet des champs/axes/entités/relations BRUTS que la plateforme mappera
    /// (ou différera). La période est un axe « DISPONIBLE DEPUIS » sous la responsabilité de
    /// l'adaptateur (aucun document ne doit devenir définitivement invisible une fois le filigrane
    /// avancé) ; l'anti-doublon <c>(source_reference, payload_hash)</c> rend toute ré-extraction idempotente.
    /// </summary>
    /// <param name="fromInclusiveUtc">Borne basse de la période (UTC, incluse).</param>
    /// <param name="toExclusiveUtc">Borne haute de la période (UTC, exclue).</param>
    /// <returns>Les documents GED ingérés de la période (différés/streaming).</returns>
    IEnumerable<IngestedDocumentDto> ExtractManagedDocuments(DateTime fromInclusiveUtc, DateTime toExclusiveUtc);

    /// <summary>
    /// Ouvre le contenu binaire d'un document GED (LECTURE SEULE), quand la source le fournit
    /// (<see cref="ManagedExtractorCapabilitiesDto.ProvidesBinaryContent"/>). L'appelant possède le flux
    /// et doit le disposer. La plateforme range ce binaire write-once au coffre (§4.3.2 / §5.1) — l'agent
    /// ne fait que le lire.
    /// </summary>
    /// <param name="sourceReference">Référence source du document dont on ouvre le contenu.</param>
    /// <returns>Un flux de lecture sur le contenu binaire (à disposer par l'appelant).</returns>
    Stream OpenContent(string sourceReference);
}
