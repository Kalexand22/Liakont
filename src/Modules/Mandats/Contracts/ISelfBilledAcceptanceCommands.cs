namespace Liakont.Modules.Mandats.Contracts;

/// <summary>
/// Pilote le cycle de vie de l'acceptation d'une auto-facture sous mandat (type 389, ADR-0024, F15 §2.3).
/// Depuis SIG05, l'acceptation est une PROJECTION restreinte du module générique DocumentApproval (purpose
/// SelfBilledAcceptance) : ce port délègue l'ÉTAT et le JOURNAL append-only (<c>document_approval_log</c>) à
/// DocumentApproval, et maintient la companion fiscale du module Mandats (BT-1 / pending_since). La machine
/// reste FERMÉE à 4 états (PendingAcceptance → {Accepted, TacitlyAccepted, Contested}, aucun retour arrière —
/// INV-ACCEPT-4) ; la bascule tacite est portée par le job (MND04), pas par ce port.
/// </summary>
/// <remarks>
/// Frontière (CLAUDE.md n°6/14, INV-MANDATS-2) : surface primitive (aucune dépendance sur DocumentApproval dans
/// l'interface). Tout est scopé par <paramref name="companyId"/> (résolu par l'appelant, jamais cross-tenant).
/// </remarks>
public interface ISelfBilledAcceptanceCommands
{
    /// <summary>
    /// Genèse : enregistre une acceptation en attente (<c>PendingAcceptance</c>) — émission bloquée tant qu'on
    /// n'a pas accepté. <paramref name="deadlineUtc"/> = échéance de bascule tacite (<c>null</c> = bascule tacite
    /// impossible : mandat tacite ou délai non renseigné). <paramref name="pendingSince"/> = instant d'entrée en
    /// attente. Lève une <c>ConflictException</c> si une acceptation non terminale existe déjà pour le document.
    /// La non-atomicité inter-stores (companion Mandats + validation DocumentApproval) est un choix assumé,
    /// fail-closed : une companion orpheline sans validation bloque l'émission (voir implémentation).
    /// </summary>
    Task OpenPendingAsync(
        Guid companyId,
        Guid documentId,
        DateTimeOffset pendingSince,
        DateTimeOffset? deadlineUtc,
        Guid? operatorId,
        string? operatorName,
        CancellationToken ct = default);

    /// <summary>Acceptation EXPRESSE (opérateur/mandant) : <c>PendingAcceptance</c> → <c>Accepted</c>. Refusée depuis un état terminal.</summary>
    Task AcceptExpresslyAsync(
        Guid companyId, Guid documentId, Guid? operatorId, string? operatorName, CancellationToken ct = default);

    /// <summary>Contestation dans le délai : <c>PendingAcceptance</c> → <c>Contested</c> (terminal ; correction = avoir + nouvelle facture).</summary>
    Task ContestAsync(
        Guid companyId, Guid documentId, Guid? operatorId, string? operatorName, CancellationToken ct = default);
}
