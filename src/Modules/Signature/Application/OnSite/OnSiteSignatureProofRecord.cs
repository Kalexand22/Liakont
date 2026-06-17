namespace Liakont.Modules.Signature.Application.OnSite;

using System;

/// <summary>
/// Entrée IMMUABLE du journal de preuve de signature sur place (ADR-0030 §3/§4/§5 ; INV-ONSITE-6). Persistée
/// en append-only (<c>signature.onsite_signature_proofs</c>, double trigger base) : la métadonnée de preuve
/// (empreinte de binding vérifiée, déposant, signataire vérifié, niveau, référence WORM de l'artefact) est
/// une piste d'audit, jamais modifiable. NE PORTE AUCUN GABARIT BIOMÉTRIQUE dérivé de la FSS (ADR-0030 §8,
/// INV-ONSITE-10) : seuls le tracé (rapatrié en WORM) et son empreinte de binding sont conservés.
/// </summary>
public sealed record OnSiteSignatureProofRecord
{
    /// <summary>Identifiant de la preuve.</summary>
    public required Guid Id { get; init; }

    /// <summary>Tenant propriétaire (clé <c>company_id</c>, NOT NULL — tenant-scopé).</summary>
    public required Guid CompanyId { get; init; }

    /// <summary>Document signé.</summary>
    public required Guid DocumentId { get; init; }

    /// <summary>Empreinte de binding VÉRIFIÉE (SHA-256 hex des octets exacts de l'artefact scellé).</summary>
    public required string BindingHash { get; init; }

    /// <summary>Le DÉPOSANT : principal authentifié ayant téléversé la capture (jamais le signataire).</summary>
    public required Guid UploaderUserId { get; init; }

    /// <summary>Le SIGNATAIRE vérifié (liaison séparée), ou <c>null</c> si aucune liaison vérifiée n'existait.</summary>
    public string? SignerIdentity { get; init; }

    /// <summary>Vrai si un signataire vérifié a été résolu (liaison séparée), jamais dérivé du déposant ni du payload.</summary>
    public required bool SignerVerified { get; init; }

    /// <summary>Niveau de preuve atteint (« SES » — jamais AES/QES par défaut, ADR-0030 §6).</summary>
    public required string Level { get; init; }

    /// <summary>Référence WORM de l'artefact de preuve rapatrié (empreinte du paquet/addendum), ou <c>null</c>.</summary>
    public string? ProofArchiveRef { get; init; }

    /// <summary>Horodatage de capture côté client (UTC).</summary>
    public required DateTimeOffset CapturedAtUtc { get; init; }
}
