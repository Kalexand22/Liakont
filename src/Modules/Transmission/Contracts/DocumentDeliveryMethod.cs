namespace Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Canal de livraison d'un document déjà construit (Factur-X scellé) par une PA de niveau « Essentiel »
/// (F16 §6.2). Sert à router l'appel vers la bonne implémentation de <see cref="IDocumentDeliveryChannel"/>
/// — toutes définies dans <c>Transmission.Contracts</c> et implémentées au Host (composition root). Le
/// plug-in PA ne connaît que l'abstraction : il ne référence ni MailKit ni le module Notification.
/// </summary>
public enum DocumentDeliveryMethod
{
    /// <summary>Email — l'artefact est une PIÈCE JOINTE d'un message MIME (F16 §6.2).</summary>
    Email = 1,

    /// <summary>Dépôt de fichier — écriture dans un dossier / point de montage par tenant (F16 §6.2).</summary>
    FileDeposit = 2,
}
