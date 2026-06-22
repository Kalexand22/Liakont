namespace Liakont.Agent.Core.Transport;

using System.Collections.Generic;
using Liakont.Agent.Contracts.Transport;

/// <summary>
/// Couture de transport vers la plateforme (contrat d'ingestion F12 §3). Surface MINCE : elle émet
/// une requête et restitue une catégorie de réponse, SANS politique (ni retry, ni re-découpe, ni
/// backoff — ceux-ci vivent dans <see cref="QueueDrainer"/>). Cela rend le client testable contre un
/// serveur HTTP mocké et concentre la décision dans une seule classe.
/// </summary>
public interface IPlatformClient
{
    /// <summary>
    /// Pousse un lot de documents. Les documents sont fournis sous leur JSON CANONIQUE déjà calculé
    /// (les octets hashés par l'anti-doublon) : la plateforme reçoit exactement l'empreinte vérifiée.
    /// </summary>
    /// <param name="canonicalDocumentJsons">JSON canonique de chaque document, dans l'ordre d'envoi.</param>
    /// <param name="sourceTaxRegimes">Régimes de TVA source (métadonnée de push, non hashée).</param>
    /// <param name="extractorCapabilities">Capacités déclarées de la source (métadonnée de push, non hashée, ADR-0004 D2 / RD401) ; <c>null</c> quand l'agent n'en transmet pas.</param>
    /// <returns>Le résultat du push (catégorie + résultats par document).</returns>
    PushBatchOutcome PushDocuments(
        IReadOnlyList<string> canonicalDocumentJsons,
        IReadOnlyList<SourceTaxRegimeDto> sourceTaxRegimes,
        ExtractorCapabilitiesDto? extractorCapabilities = null);

    /// <summary>Pousse un PDF lié à un document (POST /api/agent/v1/documents/{sourceReference}/pdf).</summary>
    /// <param name="sourceReference">Référence source du document lié.</param>
    /// <param name="filePath">Chemin du fichier PDF.</param>
    /// <returns>Le résultat du push.</returns>
    PdfPushOutcome PushLinkedPdf(string sourceReference, string filePath);

    /// <summary>Pousse un PDF du pool non lié (POST /api/agent/v1/pdf-pool).</summary>
    /// <param name="filePath">Chemin du fichier PDF.</param>
    /// <returns>Le résultat du push.</returns>
    PdfPushOutcome PushPoolPdf(string filePath);

    /// <summary>
    /// Interroge le point de statut d'un document (GET /api/agent/v1/documents/status — ADR-0012),
    /// clé <c>(sourceReference, payloadHash)</c>, lecture seule.
    /// </summary>
    /// <param name="sourceReference">Référence source du document.</param>
    /// <param name="payloadHash">Empreinte canonique du payload.</param>
    /// <returns>Le statut de prise en charge rapporté.</returns>
    DocumentStatusOutcome GetDocumentStatus(string sourceReference, string payloadHash);

    /// <summary>
    /// Émet un battement de cœur (POST /api/agent/v1/heartbeat — F12 §3.2) et restitue la
    /// configuration effective renvoyée par la plateforme. Sortie typée : un échec (réseau, clé
    /// invalide…) ne lève JAMAIS — l'agent conserve sa configuration locale et réessaie (F12 §2.5).
    /// </summary>
    /// <param name="heartbeat">L'état de l'agent à transmettre.</param>
    /// <returns>La catégorie de réponse + la configuration effective si la plateforme a répondu 200.</returns>
    HeartbeatOutcome SendHeartbeat(HeartbeatRequestDto heartbeat);

    /// <summary>
    /// Lit la configuration courante de l'agent (GET /api/agent/v1/configuration — F12 §3.2),
    /// interrogée au démarrage du service. Sortie typée : un échec ne lève JAMAIS — l'agent démarre
    /// avec sa configuration locale (F12 §2.5).
    /// </summary>
    /// <returns>La catégorie de réponse + la configuration courante si la plateforme a répondu 200.</returns>
    ConfigurationOutcome GetConfiguration();
}
