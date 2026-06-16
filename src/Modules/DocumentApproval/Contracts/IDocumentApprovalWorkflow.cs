namespace Liakont.Modules.DocumentApproval.Contracts;

/// <summary>
/// Port de COMMANDE (écriture) du workflow de validation de document, exposé à la frontière <c>Contracts</c>
/// (module-rules §3, CLAUDE.md n°6/14) pour qu'un module exposeur (ex. Mandats, ADR-0024) pilote le cycle de
/// vie d'une validation SANS tirer la persistance (<c>Application</c>/<c>Infrastructure</c>) ni la machine de
/// domaine. Chaque opération est ATOMIQUE (transition + ligne de journal append-only <c>document_approval_log</c>
/// dans la MÊME transaction — INV-APPROVAL-6) et scopée par <paramref name="companyId"/> (CLAUDE.md n°9, résolu
/// par l'appelant, jamais fourni par le client).
/// <para>
/// La surface reste <b>générique</b> (aucune sémantique fiscale) et <b>sans niveau de preuve</b> : seuls les
/// niveaux <c>Recorded</c> (enregistrée) et la bascule tacite sont exposés ici — les niveaux SES/AES/QES (preuve
/// externe d'un fournisseur de signature) passent par le plug-in de signature (SIG07), pas par ce port. Garder
/// <c>Contracts</c> sans dépendance sur <c>Signature.Contracts</c> (les niveaux ne traversent pas l'interface).
/// </para>
/// </summary>
public interface IDocumentApprovalWorkflow
{
    /// <summary>
    /// Genèse : crée la tentative initiale (état <c>PendingValidation</c>) d'un document pour un purpose et
    /// inscrit l'entrée de journal de genèse dans la MÊME transaction. <paramref name="deadlineUtc"/> porte
    /// l'échéance de bascule tacite (<c>null</c> = bascule tacite impossible). Lève une <c>ConflictException</c>
    /// (Stratum.Common) si une tentative non terminale existe déjà pour ce <c>(companyId, documentId, purpose)</c>.
    /// </summary>
    Task RequestValidationAsync(
        Guid companyId,
        Guid documentId,
        ValidationPurpose purpose,
        DateTimeOffset? deadlineUtc,
        Guid? operatorId,
        string? operatorName,
        CancellationToken ct = default);

    /// <summary>
    /// Validation EXPRESSE de niveau <c>Recorded</c> (acceptation enregistrée, sans preuve externe) :
    /// <c>PendingValidation</c> → <c>Validated</c>, avec acceptation expresse tracée (condition 3 du gate). La
    /// tentative la plus récente est verrouillée le temps de la transition. Lève si la tentative est absente ou
    /// dans un état terminal (machine fermée). Pour une preuve SES/AES/QES, passer par le fournisseur (SIG07).
    /// </summary>
    Task RecordRecordedValidationAsync(
        Guid companyId,
        Guid documentId,
        ValidationPurpose purpose,
        Guid? operatorId,
        string? operatorName,
        CancellationToken ct = default);

    /// <summary>
    /// Contestation dans le délai : <c>PendingValidation</c> → <c>Contested</c> (terminal). La tentative la plus
    /// récente est verrouillée le temps de la transition. Lève si la tentative est absente ou terminale.
    /// </summary>
    Task ContestAsync(
        Guid companyId,
        Guid documentId,
        ValidationPurpose purpose,
        Guid? operatorId,
        string? operatorName,
        CancellationToken ct = default);

    /// <summary>
    /// Bascule TACITE SI ÉLIGIBLE (transition SYSTÈME, sans opérateur), sous verrou : recharge la tentative la
    /// plus récente (<c>FOR UPDATE</c>), RE-VÉRIFIE l'éligibilité (<c>PendingValidation</c>, <c>DeadlineUtc</c>
    /// non null et ≤ <paramref name="nowUtc"/>) puis transite vers <c>TacitlyValidated</c> dans la MÊME
    /// transaction (anti-TOCTOU : l'état a pu changer entre l'énumération et le verrou). Retourne <c>true</c>
    /// si la transition a été effectuée, <c>false</c> si la tentative n'est plus éligible (no-op sans effet).
    /// </summary>
    Task<bool> RecordTacitValidationIfDueAsync(
        Guid companyId,
        Guid documentId,
        ValidationPurpose purpose,
        DateTimeOffset nowUtc,
        string? operatorName,
        CancellationToken ct = default);
}
