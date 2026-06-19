namespace Liakont.Modules.Signature.Application.OnSite;

using System;

/// <summary>
/// Liaison VÉRIFIÉE déposant→signataire pour un document (ADR-0030 §5). Établie hors de la capture, par un
/// opérateur SVV authentifié, à partir d'une identification EN PERSONNE du mandant. Tenant-scopée
/// (<c>company_id</c> NOT NULL). C'est la seule source d'un <c>SignerIdentity</c> probant : la capture la
/// résout côté serveur, jamais depuis son propre payload (INV-ONSITE-7, test d'usurpation).
/// </summary>
public sealed record OnSiteSignerBindingRecord
{
    /// <summary>Identifiant de la liaison.</summary>
    public required Guid Id { get; init; }

    /// <summary>Tenant propriétaire (clé <c>company_id</c>, NOT NULL).</summary>
    public required Guid CompanyId { get; init; }

    /// <summary>Document concerné.</summary>
    public required Guid DocumentId { get; init; }

    /// <summary>Identité du signataire vérifié (mandant).</summary>
    public required string SignerIdentity { get; init; }

    /// <summary>Méthode de vérification (ex. « identification en personne par la SVV »).</summary>
    public required string VerificationMethod { get; init; }

    /// <summary>Opérateur SVV ayant enregistré la liaison (principal authentifié).</summary>
    public required Guid RegisteredByUserId { get; init; }

    /// <summary>Horodatage d'enregistrement de la liaison (UTC).</summary>
    public required DateTimeOffset VerifiedAtUtc { get; init; }
}
