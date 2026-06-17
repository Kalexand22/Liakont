namespace Liakont.Modules.Signature.Contracts.OnSite;

/// <summary>
/// Corps de l'enregistrement d'un SIGNATAIRE VÉRIFIÉ pour un document, par un opérateur authentifié de la
/// société de ventes (ADR-0030 §5 ; F17 §6). C'est le « mécanisme de liaison VÉRIFIÉ séparé » : le mandant
/// est identifié EN PERSONNE par la SVV au guichet, puis l'opérateur consigne ici son identité. Cette étape
/// est DISTINCTE de la capture (le déposant qui téléverse n'est pas le signataire) et constitue la seule
/// source d'un <c>SignerIdentity</c> probant — jamais le payload de capture (test d'usurpation, INV-ONSITE-7).
/// Le <c>document_id</c> et le <c>company_id</c> sont résolus par le serveur (route + principal authentifié),
/// jamais portés en clair par cet objet.
/// </summary>
public sealed record RegisterVerifiedSignerRequest
{
    /// <summary>Identité du signataire vérifié (mandant), telle qu'établie en personne par la SVV. Obligatoire.</summary>
    public required string SignerIdentity { get; init; }

    /// <summary>Méthode de vérification (ex. « identification en personne par la SVV au guichet »). Obligatoire.</summary>
    public required string VerificationMethod { get; init; }
}
