namespace Liakont.Modules.FleetSupervision.Application;

/// <summary>
/// Projection minimale d'une instance pour la passe de notification de mise à jour (OPS04) : juste ce qu'il
/// faut pour décider d'envoyer l'email « nouvelle version disponible » et éviter le rebond
/// (<paramref name="NotifiedVersion"/> = dernière version pour laquelle on a déjà notifié cette instance).
/// </summary>
/// <param name="InstanceId">Identifiant opaque de l'instance.</param>
/// <param name="DisplayName">Libellé d'affichage.</param>
/// <param name="ContactEmail">Contact technique destinataire (non vide).</param>
/// <param name="Version">Version actuellement rapportée par l'instance.</param>
/// <param name="NotifiedVersion">Dernière version notifiée (null si jamais notifiée).</param>
public sealed record FleetNotificationCandidate(
    string InstanceId,
    string DisplayName,
    string ContactEmail,
    string Version,
    string? NotifiedVersion);
