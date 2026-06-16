namespace Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Demande de livraison d'un document DÉJÀ CONSTRUIT (Factur-X scellé) par une PA de niveau « Essentiel »
/// (F16 §6.2). Portée par le plug-in PA générique vers une implémentation de
/// <see cref="IDocumentDeliveryChannel"/> (au Host). Le plug-in TRANSPORTE l'artefact reçu, il ne le
/// régénère jamais (indépendance plug-in — CLAUDE.md n°6).
/// <para>
/// Type <c>class</c> volontairement (et NON <c>record</c>) : la demande peut porter une
/// <see cref="SmtpAuth"/> (secret en mémoire) ; un <c>record</c> l'imprimerait via <c>ToString()</c>
/// (risque de fuite — P1, CLAUDE.md n°18). De plus <see cref="Content"/> est un binaire non comparable
/// par valeur.
/// </para>
/// </summary>
public sealed class DocumentDeliveryRequest
{
    /// <summary>Canal de livraison (email / dépôt de fichier).</summary>
    public required DocumentDeliveryMethod Method { get; init; }

    /// <summary>
    /// Cible NON SENSIBLE de la livraison : adresse email du destinataire (boîte PA du tenant) pour
    /// <see cref="DocumentDeliveryMethod.Email"/>, ou chemin de dossier par tenant pour
    /// <see cref="DocumentDeliveryMethod.FileDeposit"/>.
    /// </summary>
    public required string Target { get; init; }

    /// <summary>Octets de l'artefact à transmettre (Factur-X PDF/A-3 scellé). Jamais régénéré par le plug-in.</summary>
    public required ReadOnlyMemory<byte> Content { get; init; }

    /// <summary>Nom de fichier de l'artefact (ex. <c>factur-x_&lt;numéro&gt;.pdf</c>).</summary>
    public required string FileName { get; init; }

    /// <summary>Type MIME de l'artefact (Factur-X = <c>application/pdf</c>).</summary>
    public string ContentType { get; init; } = "application/pdf";

    /// <summary>Sujet du message (email uniquement) — français (CLAUDE.md n°12). Ignoré pour le dépôt de fichier.</summary>
    public string? Subject { get; init; }

    /// <summary>Corps du message (email uniquement) — français. Ignoré pour le dépôt de fichier.</summary>
    public string? Body { get; init; }

    /// <summary>
    /// Identifiants SMTP par tenant (email uniquement, F16 §6.2) — déchiffrés Host-side, jamais en clair
    /// dans le descripteur. <c>null</c> ⇒ l'implémentation Host réutilise le SMTP d'instance (ADR-0018).
    /// </summary>
    public SmtpDeliveryAuthentication? SmtpAuth { get; init; }
}
