namespace Liakont.Host.Components;

/// <summary>
/// Libellé d'affichage FRANÇAIS d'un type d'événement de la piste d'audit d'un document (F10 §2.3, onglet
/// Historique). Indexé par le NOM du type d'événement (la valeur chaîne de <c>DocumentEventType</c> exposée
/// dans <c>DocumentEventDto.EventType</c>), ce qui garde l'affichage DÉCOUPLÉ de l'enum du module Documents.
/// Fonction TOTALE et PURE d'affichage (aucune règle métier interprétée, CLAUDE.md n°2/19) : un type inconnu
/// retombe sur sa valeur brute non vide — jamais d'exception, jamais d'événement masqué.
/// </summary>
public static class DocumentEventDisplay
{
    /// <summary>
    /// Libellé français d'un type d'événement (ex. <c>DocumentDetected</c> → « Détecté »). Toute valeur non
    /// reconnue retombe sur la chaîne brute (ou « — » si vide).
    /// </summary>
    /// <param name="eventType">Nom du type d'événement, tel que produit par le module Documents.</param>
    public static string For(string? eventType) => eventType switch
    {
        "DocumentDetected" => "Détecté",
        "DocumentBlocked" => "Bloqué",
        "DocumentReadyToSend" => "Prêt à envoyer",
        "DocumentSending" => "Envoi engagé",
        "DocumentIssued" => "Émis",
        "DocumentEReported" => "E-reporté",
        "DocumentRejectedByPa" => "Rejeté par la Plateforme Agréée",
        "DocumentTechnicalError" => "Erreur technique",
        "DocumentSuperseded" => "Remplacé",
        "DocumentManuallyHandled" => "Traité manuellement",
        "DocumentSourceAlteredAfterIssue" => "Source altérée après émission",
        "DocumentReconciledAuto" => "PDF rapproché automatiquement",
        "DocumentReconciledManual" => "PDF rapproché manuellement",
        "DocumentBuyerConfirmedB2C" => "Acheteur confirmé particulier (B2C)",
        "DocumentRecheckedStillBlocked" => "Re-vérifié (toujours bloqué)",
        _ => string.IsNullOrWhiteSpace(eventType) ? "—" : eventType!,
    };
}
