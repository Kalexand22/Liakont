namespace Liakont.Modules.Documents.Domain.Entities;

using System;
using Liakont.Modules.Documents.Domain.StateMachine;

/// <summary>
/// Document métier de la passerelle (F06 §3, item TRK01). Agrégat racine du module <c>Documents</c> :
/// il porte l'état du document dans la passerelle, ses montants de contrôle (en <see cref="decimal"/>,
/// CLAUDE.md n°1) et l'empreinte du payload pivot. Il vit dans la base DU TENANT (database-per-tenant,
/// blueprint §7) : aucune colonne de tenant n'est nécessaire, l'isolation est ASSURÉE PAR LA CONNEXION
/// (la connexion EST le tenant — F06 amendement stockage 2026-06-03). Le document est créé en état
/// <see cref="DocumentState.Detected"/> par l'ingestion (PIV04) ; son cycle de vie est ensuite régi par la
/// machine à états explicite (<see cref="DocumentStateMachine"/>, item TRK02). Chaque transition PRODUIT
/// automatiquement le <see cref="DocumentEvent"/> de la piste d'audit qui la matérialise — changement
/// d'état et fait d'audit forment un tout, persisté dans la même transaction (atomicité, F06 §3).
/// </summary>
/// <remarks>
/// Les montants sont ceux CALCULÉS PAR LA SOURCE (portés par le pivot) — le module Documents ne calcule
/// ni ne valide rien (frontière module-rules §2 ; le contrôle des totaux est dans Validation, F04). Le
/// <see cref="DocumentType"/> est le type BRUT de la source (la classification facture/avoir vit dans
/// Validation — ADR-0004 D3-3). Tout champ source absent reste <c>null</c> (jamais de défaut implicite
/// masquant une donnée manquante — blueprint §8).
/// </remarks>
public sealed class Document
{
    private Document()
    {
    }

    /// <summary>Identifiant du document, attribué par l'ingestion et partagé avec la réception et l'événement (PIV04).</summary>
    public Guid Id { get; private set; }

    /// <summary>Référence du document dans le système source (réconciliation + audit).</summary>
    public string SourceReference { get; private set; } = string.Empty;

    /// <summary>Numéro du document (EN 16931 BT-1) — clé fonctionnelle vers la PA. La source est le seul créateur de numéros.</summary>
    public string DocumentNumber { get; private set; } = string.Empty;

    /// <summary>Type de document BRUT porté par la source (classification facture/avoir déléguée à Validation — ADR-0004 D3-3).</summary>
    public string DocumentType { get; private set; } = string.Empty;

    /// <summary>Date d'émission (EN 16931 BT-2).</summary>
    public DateOnly IssueDate { get; private set; }

    /// <summary>SIREN du fournisseur/émetteur (EN 16931 BT-30). Absent dans la source = <c>null</c>.</summary>
    public string? SupplierSiren { get; private set; }

    /// <summary>Raison sociale / nom du destinataire (B2C sans tiers identifié = <c>null</c>).</summary>
    public string? CustomerName { get; private set; }

    /// <summary>Indice BRUT « société » porté par la source pour le destinataire (interprétation déléguée à Validation, VAL05).</summary>
    public bool CustomerIsCompanyHint { get; private set; }

    /// <summary>
    /// Verdict OPÉRATEUR « acheteur confirmé particulier (B2C) » du garde-fou B2B/B2C (item API02b, F08 §A.4) :
    /// quand <c>true</c>, la re-vérification (recheck) ne re-bloque pas le document sur <c>BUYER_LOOKS_PROFESSIONAL</c>
    /// (la décision tranchée et journalisée prime sur l'heuristique d'indice — VAL05). Défaut <c>false</c>.
    /// </summary>
    public bool BuyerConfirmedAsIndividual { get; private set; }

    /// <summary>Total HT (EN 16931 BT-109), <see cref="decimal"/>.</summary>
    public decimal TotalNet { get; private set; }

    /// <summary>Total TVA (EN 16931 BT-110), <see cref="decimal"/>.</summary>
    public decimal TotalTax { get; private set; }

    /// <summary>Total TTC (EN 16931 BT-112), <see cref="decimal"/>.</summary>
    public decimal TotalGross { get; private set; }

    /// <summary>État du document dans la passerelle (F06 §3).</summary>
    public DocumentState State { get; private set; }

    /// <summary>Empreinte canonique du payload pivot (SHA-256 hex) — anti-doublon (TRK03) et détection d'altération (F06).</summary>
    public string PayloadHash { get; private set; } = string.Empty;

    /// <summary>Identifiant du document côté Plateforme Agréée (renseigné à l'émission — Transmission). Absent = <c>null</c>.</summary>
    public string? PaDocumentId { get; private set; }

    /// <summary>Version de table de mapping TVA appliquée (F03), renseignée par le pipeline (PIP). Absent = <c>null</c>.</summary>
    public string? MappingVersion { get; private set; }

    /// <summary>Première observation du document (UTC).</summary>
    public DateTimeOffset FirstSeenUtc { get; private set; }

    /// <summary>Dernière mise à jour du document (UTC).</summary>
    public DateTimeOffset LastUpdateUtc { get; private set; }

    /// <summary>
    /// Crée un document en état <see cref="DocumentState.Detected"/> à partir des données d'un document
    /// reçu par l'ingestion (PIV04). Les montants sont conservés tels que calculés par la source.
    /// </summary>
    public static Document CreateDetected(
        Guid id,
        string sourceReference,
        string documentNumber,
        string documentType,
        DateOnly issueDate,
        string? supplierSiren,
        string? customerName,
        bool customerIsCompanyHint,
        decimal totalNet,
        decimal totalTax,
        decimal totalGross,
        string payloadHash,
        DateTimeOffset detectedAtUtc)
    {
        Require(sourceReference, nameof(sourceReference), "La référence source est obligatoire.");
        Require(documentNumber, nameof(documentNumber), "Le numéro de document est obligatoire (EN 16931 BT-1).");
        Require(documentType, nameof(documentType), "Le type de document source est obligatoire.");
        Require(payloadHash, nameof(payloadHash), "L'empreinte du payload est obligatoire.");

        // Intégrité de stockage (CLAUDE.md n°4) : la colonne numeric(18,2) tronque silencieusement tout
        // montant à plus de 2 décimales, ce qui altérerait un montant audité sans erreur visible. Ce garde-fou
        // rejette l'entrée avant persistance. Il ne juge pas la CORRECTION fiscale des montants (contrôle
        // des totaux délégué à Validation, F04) — uniquement leur stockage SANS PERTE.
        RequireMonetaryScale(totalNet, nameof(totalNet));
        RequireMonetaryScale(totalTax, nameof(totalTax));
        RequireMonetaryScale(totalGross, nameof(totalGross));

        return new Document
        {
            Id = id,
            SourceReference = sourceReference.Trim(),
            DocumentNumber = documentNumber.Trim(),
            DocumentType = documentType.Trim(),
            IssueDate = issueDate,
            SupplierSiren = NullIfBlank(supplierSiren),
            CustomerName = NullIfBlank(customerName),
            CustomerIsCompanyHint = customerIsCompanyHint,
            TotalNet = totalNet,
            TotalTax = totalTax,
            TotalGross = totalGross,
            State = DocumentState.Detected,
            PayloadHash = payloadHash.Trim(),
            PaDocumentId = null,
            MappingVersion = null,
            FirstSeenUtc = detectedAtUtc,
            LastUpdateUtc = detectedAtUtc,
        };
    }

    /// <summary>
    /// Reconstitue un document depuis la persistance (lecture). Aucune logique de transition n'est
    /// appliquée : la machine à états (TRK02) opère sur l'agrégat reconstitué.
    /// </summary>
    public static Document Reconstitute(
        Guid id,
        string sourceReference,
        string documentNumber,
        string documentType,
        DateOnly issueDate,
        string? supplierSiren,
        string? customerName,
        bool customerIsCompanyHint,
        decimal totalNet,
        decimal totalTax,
        decimal totalGross,
        DocumentState state,
        string payloadHash,
        string? paDocumentId,
        string? mappingVersion,
        DateTimeOffset firstSeenUtc,
        DateTimeOffset lastUpdateUtc,
        bool buyerConfirmedAsIndividual = false)
    {
        return new Document
        {
            Id = id,
            SourceReference = sourceReference,
            DocumentNumber = documentNumber,
            DocumentType = documentType,
            IssueDate = issueDate,
            SupplierSiren = supplierSiren,
            CustomerName = customerName,
            CustomerIsCompanyHint = customerIsCompanyHint,
            TotalNet = totalNet,
            TotalTax = totalTax,
            TotalGross = totalGross,
            State = state,
            PayloadHash = payloadHash,
            PaDocumentId = paDocumentId,
            MappingVersion = mappingVersion,
            FirstSeenUtc = firstSeenUtc,
            LastUpdateUtc = lastUpdateUtc,
            BuyerConfirmedAsIndividual = buyerConfirmedAsIndividual,
        };
    }

    // ── Machine à états (item TRK02, F06 §3) ──────────────────────────────────────────────────────────
    // Chaque transition VALIDE sa légalité via DocumentStateMachine (refus = InvalidDocumentTransitionException
    // AVANT toute mutation), change l'état, avance LastUpdateUtc, et RETOURNE le DocumentEvent qui matérialise
    // la transition. Le retour n'est pas optionnel : on ne peut pas transiter sans obtenir son fait d'audit, que
    // l'appelant persiste dans la MÊME transaction que l'état (atomicité — UoW du module).

    /// <summary>Detected → Blocked : une validation pré-envoi a refusé le document. <paramref name="reason"/> = motif de blocage (optionnel, journalisé).</summary>
    public DocumentEvent MarkBlocked(DateTimeOffset occurredAtUtc, string? reason = null)
        => ApplyTransition(DocumentState.Blocked, DocumentEventType.DocumentBlocked, occurredAtUtc, reason, operatorIdentity: null);

    /// <summary>→ ReadyToSend : prêt à être transmis (depuis Detected, depuis Blocked après correction source, ou reprise depuis TechnicalError).</summary>
    public DocumentEvent MarkReadyToSend(DateTimeOffset occurredAtUtc, string? detail = null)
        => ApplyTransition(DocumentState.ReadyToSend, DocumentEventType.DocumentReadyToSend, occurredAtUtc, detail, operatorIdentity: null);

    /// <summary>
    /// → ReadyToSend en CONSIGNANT la version de table de mapping TVA appliquée (F03/F06 §3, PIP01) : le
    /// mapping a été résolu et la version est tracée sur le document (justification de la TVA appliquée).
    /// <paramref name="mappingVersion"/> est OBLIGATOIRE. NOM DISTINCT (et non une surcharge de
    /// <see cref="MarkReadyToSend(DateTimeOffset, string?)"/>) afin qu'aucun appelant ne puisse passer la
    /// version en positionnel et la voir liée au paramètre <c>detail</c> — perte de traçabilité F03 silencieuse.
    /// </summary>
    public DocumentEvent MarkReadyToSendWithMapping(DateTimeOffset occurredAtUtc, string mappingVersion, string? detail = null, string? operatorIdentity = null)
    {
        var version = RequireText(
            mappingVersion,
            nameof(mappingVersion),
            "La version de table de mapping TVA appliquée est obligatoire au passage ReadyToSend (traçabilité F03/F06 §3).");

        // Garde de légalité AVANT toute mutation (cohérent avec les autres transitions, F06 §3) : la version
        // n'est consignée qu'une fois la transition ReadyToSend acceptée — une transition refusée ne laisse
        // aucune trace, même en mémoire. <paramref name="operatorIdentity"/> trace l'opérateur quand le
        // déblocage est une action de re-vérification (FIX02) ; <c>null</c> pour un déblocage système (pipeline).
        var documentEvent = ApplyTransition(DocumentState.ReadyToSend, DocumentEventType.DocumentReadyToSend, occurredAtUtc, detail, operatorIdentity);
        MappingVersion = version;
        return documentEvent;
    }

    /// <summary>ReadyToSend → Sending : la transmission à la Plateforme Agréée est engagée.</summary>
    public DocumentEvent BeginSending(DateTimeOffset occurredAtUtc, string? detail = null)
        => ApplyTransition(DocumentState.Sending, DocumentEventType.DocumentSending, occurredAtUtc, detail, operatorIdentity: null);

    /// <summary>
    /// Sending → Issued : émission acceptée par la Plateforme Agréée (état d'émission réussie, sans suite).
    /// Les <paramref name="snapshots"/> de preuve (payload transmis, réponse PA brute, trace de mapping TVA)
    /// sont OBLIGATOIRES et portés par le <see cref="DocumentEvent"/> d'émission (item TRK04, F06 §3) : on
    /// n'enregistre jamais une émission sans sa preuve complète.
    /// </summary>
    public DocumentEvent MarkIssued(IssuanceSnapshots snapshots, DateTimeOffset occurredAtUtc, string? detail = null)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        // Référence PA attribuée à l'émission — clé de récupération aval (facture générée, tax reports : SYNC/PIP01d).
        // On ne l'écrase JAMAIS par null (une finalisation anti-doublon sans id ne doit pas effacer une référence connue).
        if (!string.IsNullOrWhiteSpace(snapshots.PaDocumentId))
        {
            PaDocumentId = snapshots.PaDocumentId;
        }

        return ApplyIssuanceTransition(
            DocumentState.Issued,
            DocumentEventType.DocumentIssued,
            occurredAtUtc,
            detail,
            snapshots.PayloadSnapshot,
            snapshots.PaResponseSnapshot,
            snapshots.MappingTrace);
    }

    /// <summary>
    /// Sending → RejectedByPa : la Plateforme Agréée a rejeté le document. Les <paramref name="snapshots"/>
    /// de la tentative (payload envoyé + réponse de rejet brute) sont OBLIGATOIRES et archivés dans le
    /// <see cref="DocumentEvent"/> de rejet (item TRK04, F06 §3) : un contrôle fiscal peut exiger de prouver
    /// ce qui a été TENTÉ. <paramref name="reason"/> = motif de rejet lisible (optionnel, journalisé en plus).
    /// </summary>
    public DocumentEvent MarkRejectedByPa(RejectionSnapshots snapshots, DateTimeOffset occurredAtUtc, string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        return ApplyIssuanceTransition(
            DocumentState.RejectedByPa,
            DocumentEventType.DocumentRejectedByPa,
            occurredAtUtc,
            reason,
            snapshots.PayloadSnapshot,
            snapshots.PaResponseSnapshot,
            mappingTrace: null);
    }

    /// <summary>Sending → TechnicalError : erreur technique de transmission, re-tentable (TechnicalError → ReadyToSend au prochain traitement).</summary>
    public DocumentEvent MarkTechnicalError(DateTimeOffset occurredAtUtc, string? detail = null)
        => ApplyTransition(DocumentState.TechnicalError, DocumentEventType.DocumentTechnicalError, occurredAtUtc, detail, operatorIdentity: null);

    /// <summary>
    /// → ManuallyHandled (état TERMINAL) : action OPÉRATEUR « traité manuellement hors passerelle » (depuis
    /// Blocked ou RejectedByPa). Le <paramref name="reason"/> est OBLIGATOIRE (motif journalisé, F06 §3) et
    /// l'identité de l'opérateur tracée. Cas : avoir orphelin, document non transmissible.
    /// </summary>
    public DocumentEvent MarkManuallyHandled(string reason, string operatorIdentity, DateTimeOffset occurredAtUtc)
    {
        var motif = RequireText(
            reason,
            nameof(reason),
            "Le motif du traitement manuel est obligatoire et journalisé (F06 §3 / TRK02).");
        var op = RequireText(
            operatorIdentity,
            nameof(operatorIdentity),
            "L'identité de l'opérateur est obligatoire pour une action de traitement manuel (piste d'audit, F06 §3).");

        return ApplyTransition(
            DocumentState.ManuallyHandled,
            DocumentEventType.DocumentManuallyHandled,
            occurredAtUtc,
            $"Traité manuellement hors passerelle. Motif : {motif}",
            op);
    }

    /// <summary>
    /// RejectedByPa → Superseded (état TERMINAL) : action OPÉRATEUR liant le document rejeté à son remplaçant.
    /// La <paramref name="replacementReference"/> (numéro/référence du nouveau document créé par le LOGICIEL
    /// SOURCE — seul créateur de numéros, F06 §4) est OBLIGATOIRE et inscrite dans la piste d'audit ; l'identité
    /// de l'opérateur est tracée. La passerelle n'invente jamais de numéro de remplacement (amendement F05 du
    /// 2026-06-03 : remplace le « suffixe -R1 »).
    /// </summary>
    public DocumentEvent Supersede(string replacementReference, string operatorIdentity, DateTimeOffset occurredAtUtc)
    {
        var remplacant = RequireText(
            replacementReference,
            nameof(replacementReference),
            "La référence du document de remplacement est obligatoire (lien vers le remplaçant, F06 §4 / TRK02).");
        var op = RequireText(
            operatorIdentity,
            nameof(operatorIdentity),
            "L'identité de l'opérateur est obligatoire pour lier un document à son remplaçant (piste d'audit, F06 §3).");

        return ApplyTransition(
            DocumentState.Superseded,
            DocumentEventType.DocumentSuperseded,
            occurredAtUtc,
            $"Remplacé par le document « {remplacant} » (la source est le seul créateur de numéros, F06 §4).",
            op);
    }

    /// <summary>
    /// Verdict OPÉRATEUR « confirmer particulier (B2C) » du garde-fou B2B/B2C (item API02b, F08 §A.4) : depuis
    /// l'état <see cref="DocumentState.Blocked"/>, enregistre la décision tranchée que l'acheteur est un
    /// particulier malgré l'indice professionnel (VAL05). NE CHANGE PAS l'état — le document reste
    /// <c>Blocked</c> jusqu'à la re-vérification (recheck), qui ne re-bloquera plus sur le garde-fou. Le
    /// marqueur persistant <see cref="BuyerConfirmedAsIndividual"/> est posé, l'horodatage avancé, et le fait
    /// d'audit append-only (identité de l'opérateur OBLIGATOIRE — décision jamais anonyme) retourné, persisté
    /// dans la même transaction. Refusé hors de l'état <c>Blocked</c> (le verdict ne s'applique qu'à un
    /// document bloqué par le garde-fou — cohérent avec la pré-vérification de l'endpoint API02b).
    /// </summary>
    public DocumentEvent ConfirmBuyerAsIndividual(string operatorIdentity, DateTimeOffset occurredAtUtc)
    {
        var op = RequireText(
            operatorIdentity,
            nameof(operatorIdentity),
            "L'identité de l'opérateur est obligatoire pour un verdict de garde-fou B2B/B2C (piste d'audit, F06 §3).");

        if (State != DocumentState.Blocked)
        {
            throw new InvalidOperationException(
                $"Le verdict « confirmer particulier (B2C) » ne s'applique qu'à un document bloqué (état actuel : {State}).");
        }

        BuyerConfirmedAsIndividual = true;
        LastUpdateUtc = occurredAtUtc;

        return DocumentEvent.BuyerConfirmedAsIndividual(Id, occurredAtUtc, op);
    }

    /// <summary>
    /// Trace une RE-VÉRIFICATION OPÉRATEUR ayant laissé le document BLOQUÉ (item FIX02) : depuis l'état
    /// <see cref="DocumentState.Blocked"/>, inscrit le fait d'audit append-only du recheck (identité de
    /// l'opérateur OBLIGATOIRE, motif RÉÉVALUÉ porté) SANS changer l'état (la machine à états interdit
    /// <c>Blocked → Blocked</c>). L'horodatage de mise à jour est avancé. Le motif inscrit devient le motif
    /// COURANT affiché (dernier évalué). Refusé hors de l'état <c>Blocked</c> (un recheck-toujours-bloqué ne
    /// concerne qu'un document bloqué — cohérent avec la pré-vérification de la re-vérification).
    /// </summary>
    public DocumentEvent RecordRecheckStillBlocked(string reevaluatedReason, string operatorIdentity, DateTimeOffset occurredAtUtc)
    {
        var motif = RequireText(
            reevaluatedReason,
            nameof(reevaluatedReason),
            "Le motif réévalué est obligatoire pour tracer une re-vérification toujours bloquée (piste d'audit, F06 §3).");
        var op = RequireText(
            operatorIdentity,
            nameof(operatorIdentity),
            "L'identité de l'opérateur est obligatoire pour une re-vérification (piste d'audit, F06 §3).");

        if (State != DocumentState.Blocked)
        {
            throw new InvalidOperationException(
                $"Une re-vérification toujours bloquée ne se trace que sur un document bloqué (état actuel : {State}).");
        }

        LastUpdateUtc = occurredAtUtc;

        return DocumentEvent.RecheckedStillBlocked(Id, occurredAtUtc, motif, op);
    }

    private static string RequireText(string value, string paramName, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(message, paramName);
        }

        return value.Trim();
    }

    private static string? NullIfBlank(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static void Require(string value, string paramName, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(message, paramName);
        }
    }

    private static void RequireMonetaryScale(decimal value, string paramName)
    {
        if (decimal.Round(value, 2) != value)
        {
            throw new ArgumentException(
                $"Le montant '{paramName}' ({value}) dépasse 2 décimales : la colonne numeric(18,2) le tronquerait silencieusement, altérant un montant audité (CLAUDE.md n°4).",
                paramName);
        }
    }

    /// <summary>
    /// Cœur de la machine à états : contrôle la légalité de la transition <c>State → target</c> (refus AVANT
    /// toute mutation), applique le nouvel état, avance l'horodatage de mise à jour, et retourne le
    /// <see cref="DocumentEvent"/> d'audit (provenance <c>from → to</c> systématiquement tracée dans le détail).
    /// </summary>
    private DocumentEvent ApplyTransition(
        DocumentState target,
        DocumentEventType eventType,
        DateTimeOffset occurredAtUtc,
        string? detail,
        string? operatorIdentity)
    {
        var from = State;
        DocumentStateMachine.EnsureCanTransition(from, target);

        State = target;
        LastUpdateUtc = occurredAtUtc;

        var auditDetail = string.IsNullOrWhiteSpace(detail)
            ? $"Transition {from} → {target}."
            : $"Transition {from} → {target}. {detail.Trim()}";

        return DocumentEvent.Transition(Id, eventType, occurredAtUtc, auditDetail, operatorIdentity);
    }

    /// <summary>
    /// Variante de <see cref="ApplyTransition"/> pour les transitions d'ÉMISSION et de REJET (item TRK04) :
    /// même contrôle de légalité AVANT toute mutation, mais l'événement d'audit produit PORTE LES SNAPSHOTS de
    /// preuve (payload transmis, réponse PA, et trace de mapping pour une émission ; <paramref name="mappingTrace"/>
    /// <c>null</c> pour un rejet). La légalité est vérifiée d'abord : une transition refusée ne capture aucun snapshot.
    /// </summary>
    private DocumentEvent ApplyIssuanceTransition(
        DocumentState target,
        DocumentEventType eventType,
        DateTimeOffset occurredAtUtc,
        string? detail,
        string payloadSnapshot,
        string paResponseSnapshot,
        string? mappingTrace)
    {
        var from = State;
        DocumentStateMachine.EnsureCanTransition(from, target);

        State = target;
        LastUpdateUtc = occurredAtUtc;

        var auditDetail = string.IsNullOrWhiteSpace(detail)
            ? $"Transition {from} → {target}."
            : $"Transition {from} → {target}. {detail.Trim()}";

        return DocumentEvent.IssuanceTransition(
            Id,
            eventType,
            occurredAtUtc,
            auditDetail,
            payloadSnapshot,
            paResponseSnapshot,
            mappingTrace);
    }
}
