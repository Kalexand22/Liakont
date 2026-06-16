namespace Liakont.Modules.DocumentApproval.Application;

using Liakont.Modules.DocumentApproval.Contracts;
using Liakont.Modules.DocumentApproval.Domain.Entities;

/// <summary>
/// Unité de travail transactionnelle du workflow de validation (ADR-0028). Toutes les écritures sont scopées
/// par le <c>company_id</c> porté par l'agrégat (CLAUDE.md n°9). CHAQUE transition (création incluse) persiste
/// l'agrégat (+ ses slots) ET son entrée de journal append-only (<c>document_approval_log</c>) dans la MÊME
/// transaction (atomicité — « pas de transition sans ligne de journal », INV-APPROVAL-6).
/// </summary>
public interface IDocumentValidationUnitOfWork : IAsyncDisposable
{
    /// <summary>
    /// Insère une tentative (état initial <see cref="ValidationState.PendingValidation"/>) + ses slots + son
    /// entrée de journal de genèse dans la même transaction. Lève une <c>ConflictException</c> (Stratum.Common)
    /// si une tentative non terminale existe déjà pour ce <c>(company_id, document_id, validation_purpose)</c>
    /// (index unique partiel — INV-APPROVAL-5) ou si la tentative <c>(…, attempt)</c> existe déjà.
    /// </summary>
    Task InsertAsync(DocumentValidation validation, DocumentApprovalLogEntry logEntry, CancellationToken ct = default);

    /// <summary>
    /// Charge une tentative précise pour transition, en VERROUILLANT sa ligne (<c>FOR UPDATE</c>) : deux
    /// transitions concurrentes sont sérialisées. <c>null</c> si absente pour ce tenant.
    /// </summary>
    Task<DocumentValidation?> GetForUpdateAsync(
        Guid companyId, Guid documentId, ValidationPurpose purpose, int attempt, CancellationToken ct = default);

    /// <summary>
    /// Charge la tentative la PLUS RÉCENTE (<c>attempt</c> max — ADR-0028 §6) pour transition, en VERROUILLANT
    /// sa ligne (<c>FOR UPDATE</c>). <c>null</c> si aucune tentative n'existe pour ce tenant.
    /// </summary>
    Task<DocumentValidation?> GetLatestForUpdateAsync(
        Guid companyId, Guid documentId, ValidationPurpose purpose, CancellationToken ct = default);

    /// <summary>
    /// Persiste ATOMIQUEMENT une transition d'état (+ l'état des slots) et son entrée de journal : met à jour
    /// l'agrégat, réécrit ses slots et insère <paramref name="logEntry"/> dans le journal append-only — le tout
    /// dans la même transaction (un échec avant <see cref="CommitAsync"/> ne laisse rien).
    /// </summary>
    Task SaveTransitionAsync(DocumentValidation validation, DocumentApprovalLogEntry logEntry, CancellationToken ct = default);

    /// <summary>
    /// Crée la tentative N+1 d'un purpose de SIGNATURE (ré-essai, ADR-0028 §6/INV-APPROVAL-5). <b>Garde
    /// anti-race</b> : verrouille la tentative N (<c>FOR UPDATE</c>) et n'autorise la création que si N est un
    /// ÉCHEC TERMINAL (<see cref="ValidationState.Expired"/>/<see cref="ValidationState.Rejected"/>) — dans la
    /// MÊME transaction (un succès concurrent de N ne peut donc pas être masqué). Lève
    /// <see cref="InvalidOperationException"/> si le purpose est exclu du ré-essai, si aucune tentative
    /// n'existe, ou si la dernière n'est pas un échec terminal ; lève <c>ConflictException</c> si une tentative
    /// non terminale concurrente existe déjà (index unique partiel). Retourne la nouvelle tentative créée.
    /// </summary>
    Task<DocumentValidation> CreateNextAttemptAsync(
        Guid companyId,
        Guid documentId,
        ValidationPurpose purpose,
        DateTimeOffset? deadlineUtc,
        IEnumerable<string>? signerIds,
        Guid? operatorId,
        string? operatorName,
        CancellationToken ct = default);

    /// <summary>Valide la transaction.</summary>
    Task CommitAsync(CancellationToken ct = default);
}

/// <summary>Fabrique d'unités de travail du workflow de validation.</summary>
public interface IDocumentValidationUnitOfWorkFactory
{
    /// <summary>Ouvre une nouvelle unité de travail (connexion + transaction).</summary>
    Task<IDocumentValidationUnitOfWork> BeginAsync(CancellationToken ct = default);
}
