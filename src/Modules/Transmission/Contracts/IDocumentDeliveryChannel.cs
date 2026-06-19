namespace Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Canal de livraison d'un document déjà construit (F16 §6.2) — l'abstraction par laquelle une PA de
/// niveau « Essentiel » (plug-in générique) TRANSMET un Factur-X scellé sans rien savoir du transport
/// concret. Définie dans <c>Transmission.Contracts</c> et IMPLÉMENTÉE AU HOST (composition root) :
/// l'email compose un MIME AVEC pièce jointe (MimeKit, Host-only — le socle <c>IEmailTransport</c> ne
/// porte qu'un corps texte et n'est PAS modifié, CLAUDE.md n°11/20) ; le dépôt de fichier écrit dans un
/// dossier par tenant. Le plug-in ne référence ni MailKit ni le module Notification : il ne voit que ce
/// contrat (frontière vérifiée par les BoundaryTests du plug-in).
/// </summary>
public interface IDocumentDeliveryChannel
{
    /// <summary>Canal servi par cette implémentation — sert au plug-in à sélectionner le bon transport.</summary>
    DocumentDeliveryMethod Method { get; }

    /// <summary>
    /// Livre l'artefact décrit par <paramref name="request"/>. Lève si la cible est invalide ou si le
    /// transport échoue (on bloque plutôt que d'émettre faux — CLAUDE.md n°3) ; le secret éventuel
    /// (<see cref="DocumentDeliveryRequest.SmtpAuth"/>) n'est jamais journalisé (CLAUDE.md n°18).
    /// </summary>
    /// <param name="request">Cible, octets, nom de fichier et métadonnées de la livraison.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    Task DeliverAsync(DocumentDeliveryRequest request, CancellationToken cancellationToken = default);
}
