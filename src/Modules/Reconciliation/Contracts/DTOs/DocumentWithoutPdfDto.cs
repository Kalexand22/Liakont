namespace Liakont.Modules.Reconciliation.Contracts.DTOs;

using System;

/// <summary>
/// Document ÉMIS pour lequel aucun PDF n'a (encore) été rapproché (item TRK07, F06 §7 §3). Surface la
/// troisième catégorie de la file d'attente : « documents émis sans PDF ». Montant en <c>decimal</c>.
/// </summary>
public sealed record DocumentWithoutPdfDto(
    Guid DocumentId,
    string DocumentNumber,
    DateOnly IssueDate,
    decimal TotalGross);
