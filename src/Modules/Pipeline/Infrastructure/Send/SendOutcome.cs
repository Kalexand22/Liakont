namespace Liakont.Modules.Pipeline.Infrastructure.Send;

/// <summary>Issue du traitement d'un document par le SEND, agrégée dans la trace d'exécution.</summary>
internal enum SendOutcome
{
    /// <summary>Document émis (Issued) — archivé, staging purgé si le paquet WORM est présent.</summary>
    Succeeded,

    /// <summary>Échec d'envoi (rejet PA / erreur technique / intégrité) — re-tentable ou à corriger.</summary>
    Failed,

    /// <summary>Différé : contenu pas encore stagé (transitoire) — repris au prochain cycle.</summary>
    Deferred,

    /// <summary>Ignoré : état changé entre-temps, ou avoir sans capacité PA (maintenu ReadyToSend).</summary>
    Skipped,
}
