namespace Liakont.Modules.Pipeline.Infrastructure.Send;

/// <summary>Issue du traitement d'un document par le SEND, agrégée dans la trace d'exécution.</summary>
internal enum SendOutcome
{
    /// <summary>Document émis (Issued) — archivé, staging purgé si le paquet WORM est présent.</summary>
    Succeeded,

    /// <summary>Échec d'envoi (rejet PA / erreur technique / intégrité) — re-tentable ou à corriger.</summary>
    Failed,

    /// <summary>Différé : contenu pas encore stagé, ou dépôt asynchrone accepté en cours d'émission
    /// (transitoire) — repris au prochain cycle SANS action opérateur.</summary>
    Deferred,

    /// <summary>En attente d'une ACTION OPÉRATEUR : émetteur non résolu (SIREN non publié) ou catégorie TVA
    /// non reposée (table de mapping modifiée depuis le CHECK). HOLD repris dès que le profil / la table sont
    /// rétablis — distinct du différé transitoire : ne JAMAIS le présenter comme « en cours d'émission »
    /// (succès silencieux, CLAUDE.md n°3) ; c'est un résultat SIGNALÉ avec action corrective (n°12).</summary>
    Held,

    /// <summary>Ignoré : état changé entre-temps, ou avoir sans capacité PA (maintenu ReadyToSend).</summary>
    Skipped,
}
