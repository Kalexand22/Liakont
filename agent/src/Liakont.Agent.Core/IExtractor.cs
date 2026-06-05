namespace Liakont.Agent.Core;

using System;
using System.Collections.Generic;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Transport;
using Liakont.Agent.Core.Extraction;

/// <summary>
/// Contrat d'un extracteur de source legacy (plug-in côté agent — F01-F02 §4). Chaque adaptateur du
/// lot ADP (EncheresV6, puis NAV, Axelor…) implémente cette interface. RÈGLES ABSOLUES du contrat
/// (F01-F02 §4.2) : lecture seule stricte (R1, aucune écriture/verrou) ; idempotence (R2, deux
/// extractions d'une même période renvoient les mêmes <see cref="PivotDocumentDto.SourceReference"/>) ;
/// l'adaptateur ne mappe PAS la TVA (R3) et ne VALIDE PAS (R4 — <c>null</c> sur l'absent) ; il
/// n'appelle jamais la PA (R5) ; aucun état interne d'envoi (R6) ; erreurs typées (R7) ; streaming (R8).
/// Toute l'intelligence (TVA, validation, états) vit sur la plateforme — l'agent n'a AUCUNE logique
/// métier (CLAUDE.md n°6).
/// </summary>
public interface IExtractor
{
    /// <summary>Nom de la source extraite (identifiant du plug-in, ex. « EncheresV6 »).</summary>
    string SourceName { get; }

    /// <summary>Capacités DÉCLARÉES de la source (ADR-0004 D2) — la plateforme s'y adapte, jamais par <c>if (source is …)</c>.</summary>
    ExtractorCapabilities Capabilities { get; }

    /// <summary>Identité de l'adaptateur (nom, version, système cible) — affichée et journalisée.</summary>
    /// <returns>L'identité de l'extracteur.</returns>
    ExtractorInfo GetInfo();

    /// <summary>Vérifie l'accès à la source (connexion, droits, schéma attendu). Ne lit aucune donnée métier.</summary>
    /// <returns>Le résultat du contrôle d'accès.</returns>
    HealthCheckResult CheckHealth();

    /// <summary>
    /// Extrait les documents (factures/avoirs) d'une période, en LECTURE SEULE et par streaming
    /// (R8). Idempotent (R2). Lève <see cref="SourceUnavailableException"/> (réessayable) ou
    /// <see cref="SourceSchemaException"/> (fatale).
    /// </summary>
    /// <param name="fromInclusiveUtc">Borne basse de la période (UTC, incluse).</param>
    /// <param name="toExclusiveUtc">Borne haute de la période (UTC, exclue).</param>
    /// <returns>Les documents pivot de la période (différés/streaming).</returns>
    IEnumerable<PivotDocumentDto> ExtractDocuments(DateTime fromInclusiveUtc, DateTime toExclusiveUtc);

    /// <summary>Extrait les encaissements d'une période (e-reporting de paiement F09), en LECTURE SEULE.</summary>
    /// <param name="fromInclusiveUtc">Borne basse de la période (UTC, incluse).</param>
    /// <param name="toExclusiveUtc">Borne haute de la période (UTC, exclue).</param>
    /// <returns>Les encaissements bruts de la période (différés/streaming).</returns>
    IEnumerable<PivotPaymentDto> ExtractPayments(DateTime fromInclusiveUtc, DateTime toExclusiveUtc);

    /// <summary>
    /// Liste les régimes de TVA du système source (code BRUT + libellé), pour alimenter le
    /// paramétrage de la table de mapping (F03) et détecter les régimes non couverts (TVA03).
    /// </summary>
    /// <returns>Les régimes de TVA source observés.</returns>
    IReadOnlyList<SourceTaxRegimeDto> ListSourceTaxRegimes();

    /// <summary>
    /// Pièces jointes (PDF) liées à un document. Renvoie une liste VIDE — jamais d'exception — quand
    /// la capacité <see cref="ExtractorCapabilities.ProvidesSourceDocuments"/> n'est pas déclarée.
    /// </summary>
    /// <param name="sourceReference">Référence source du document concerné.</param>
    /// <returns>Les pièces jointes liées (vide si la capacité est absente).</returns>
    IReadOnlyList<SourceAttachment> GetAttachments(string sourceReference);

    /// <summary>
    /// Liste les PDF d'un pool NON lié déposés sur la période (capacité
    /// <see cref="ExtractorCapabilities.ProvidesUnlinkedDocumentPool"/>), par streaming. Vide si la
    /// capacité n'est pas déclarée.
    /// </summary>
    /// <param name="fromInclusiveUtc">Borne basse de la période (UTC, incluse).</param>
    /// <param name="toExclusiveUtc">Borne haute de la période (UTC, exclue).</param>
    /// <returns>Les documents de pool de la période (différés/streaming).</returns>
    IEnumerable<PoolDocument> ListPoolDocuments(DateTime fromInclusiveUtc, DateTime toExclusiveUtc);
}
