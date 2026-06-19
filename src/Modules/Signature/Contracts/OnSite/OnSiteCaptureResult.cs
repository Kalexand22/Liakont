namespace Liakont.Modules.Signature.Contracts.OnSite;

using System;

/// <summary>
/// Résultat du traitement d'une capture sur place par le proxy <c>OnSiteCapture</c> (ADR-0030 §3/§4/§5).
/// Le binding (re-hash == hash signé) et la résolution du signataire vérifié sont des FAITS rapportés au
/// client : ce dernier ne décide rien (pur capteur). Le niveau reste <see cref="Level"/> = SES tant que
/// l'AES n'est pas auditée (ADR-0030 §6, INV-ONSITE-8) — c'est une chaîne pour rester lisible côté client
/// (aucune fuite de l'énumération du module).
/// </summary>
public sealed record OnSiteCaptureResult
{
    /// <summary>Identifiant de la preuve enregistrée (journal append-only) quand le binding est vérifié, sinon <c>null</c>.</summary>
    public Guid? ProofId { get; init; }

    /// <summary>Vrai si <c>re-hash == hash signé</c> sur les octets exacts de l'artefact scellé (ADR-0030 §4).</summary>
    public required bool BindingVerified { get; init; }

    /// <summary>
    /// Vrai si un signataire VÉRIFIÉ (liaison séparée, identification en personne par la SVV) a été résolu
    /// côté serveur pour ce document (ADR-0030 §5). Faux si aucune liaison vérifiée n'existe — la capture est
    /// alors enregistrée mais le signataire reste non prouvé (le niveau ne monte jamais au-delà de SES).
    /// </summary>
    public required bool SignerIdentityVerified { get; init; }

    /// <summary>Niveau de preuve atteint (ex. « SES ») — jamais AES/QES par défaut (ADR-0030 §6).</summary>
    public required string Level { get; init; }

    /// <summary>Message opérateur en français (CLAUDE.md n°12) décrivant l'issue de la capture.</summary>
    public required string Message { get; init; }
}
