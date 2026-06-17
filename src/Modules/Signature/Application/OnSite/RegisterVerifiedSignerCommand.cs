namespace Liakont.Modules.Signature.Application.OnSite;

using System;
using Stratum.Common.Abstractions.Messaging;

/// <summary>
/// Commande d'enregistrement d'un SIGNATAIRE VÉRIFIÉ pour un document (ADR-0030 §5). C'est le « mécanisme de
/// liaison VÉRIFIÉ séparé » : un opérateur authentifié de la SVV consigne l'identité du mandant identifié EN
/// PERSONNE au guichet. DISTINCT de la capture (le déposant ≠ le signataire) et seule source d'un
/// <c>SignerIdentity</c> probant (INV-ONSITE-7). <see cref="CompanyId"/> et <see cref="RegisteredByUserId"/>
/// sont résolus du principal authentifié (jamais du payload). Renvoie l'identifiant de la liaison créée.
/// </summary>
public sealed record RegisterVerifiedSignerCommand : ICommand<Guid>
{
    /// <summary>Tenant authentifié (re-vérifié contre le document).</summary>
    public required Guid CompanyId { get; init; }

    /// <summary>Opérateur SVV qui enregistre la liaison (principal authentifié).</summary>
    public required Guid RegisteredByUserId { get; init; }

    /// <summary>Document pour lequel le signataire est vérifié.</summary>
    public required Guid DocumentId { get; init; }

    /// <summary>Identité du signataire vérifié (mandant).</summary>
    public required string SignerIdentity { get; init; }

    /// <summary>Méthode de vérification (ex. « identification en personne par la SVV au guichet »).</summary>
    public required string VerificationMethod { get; init; }
}
