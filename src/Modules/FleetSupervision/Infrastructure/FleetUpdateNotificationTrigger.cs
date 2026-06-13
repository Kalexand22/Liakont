namespace Liakont.Modules.FleetSupervision.Infrastructure;

/// <summary>
/// Déclencheur du job SYSTÈME de notification de mise à jour (OPS04, rôle CENTRAL). Planifié par le module
/// <c>Job</c> ; la fréquence est un paramétrage de déploiement. Marqueur sans donnée : son handler parcourt
/// les instances self-hosted en retard sur la dernière version publiée et envoie l'email « nouvelle version
/// disponible » (anti-rebond par version notifiée).
/// </summary>
public sealed record FleetUpdateNotificationTrigger;
