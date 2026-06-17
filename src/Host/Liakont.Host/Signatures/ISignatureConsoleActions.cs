namespace Liakont.Host.Signatures;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.DocumentApproval.Contracts;

/// <summary>
/// Composition en ÉCRITURE de la page console des signatures (SIG10, F17 §0) : déclencher une demande de
/// validation, enregistrer une acceptation (enregistrée), contester dans le délai. Pendant en écriture de
/// <see cref="ISignatureConsoleQueries"/> — appelée IN-PROCESS depuis le circuit serveur de la console
/// (jamais par bouclage HTTP, précédent WEB05). AUCUNE machine à états ni règle fiscale n'est dupliquée : le
/// service vérifie la permission (<c>liakont.actions</c>, défense en profondeur), résout le tenant et
/// l'opérateur, puis délègue au port générique <see cref="IDocumentApprovalWorkflow"/> (la machine fermée et
/// le journal append-only restent dans le module DocumentApproval, ADR-0028). Chaque méthode renvoie un
/// RÉSULTAT (jamais d'exception sur un refus métier) avec un message opérateur français (CLAUDE.md n°12).
/// TENANT-SCOPÉ (CLAUDE.md n°9/17).
/// </summary>
internal interface ISignatureConsoleActions
{
    /// <summary>
    /// Genèse : crée une demande de validation (état <c>PendingValidation</c>) pour un document et une finalité.
    /// <paramref name="deadlineUtc"/> porte l'échéance de bascule tacite (<c>null</c> = pas de bascule tacite).
    /// Refuse proprement si une demande non terminale existe déjà pour ce (document, finalité).
    /// </summary>
    Task<SignatureActionResult> RequestValidationAsync(
        Guid documentId, ValidationPurpose purpose, DateTimeOffset? deadlineUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enregistre une acceptation EXPRESSE de niveau <c>Recorded</c> (sans preuve externe) sur la tentative en
    /// attente : <c>PendingValidation → Validated</c>. Refuse proprement si aucune tentative en attente n'existe.
    /// Pour une preuve SES/AES/QES, c'est le fournisseur de signature qui rattache la preuve (SIG06/SIG07).
    /// </summary>
    Task<SignatureActionResult> RecordRecordedAsync(
        Guid documentId, ValidationPurpose purpose, CancellationToken cancellationToken = default);

    /// <summary>
    /// Conteste dans le délai la tentative en attente : <c>PendingValidation → Contested</c> (terminal). Refuse
    /// proprement si aucune tentative en attente n'existe.
    /// </summary>
    Task<SignatureActionResult> ContestAsync(
        Guid documentId, ValidationPurpose purpose, CancellationToken cancellationToken = default);
}
