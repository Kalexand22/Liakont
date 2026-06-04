namespace Liakont.Modules.Reconciliation.Domain;

using System;

/// <summary>
/// Document ÉMIS candidat à un rapprochement (item TRK07, F06 §7). Projection minimale d'un document du
/// tenant nécessaire au moteur de rapprochement : son numéro (stratégies 1-2), sa date d'émission et son
/// montant TTC (stratégie 3). Montant en <c>decimal</c> (CLAUDE.md n°1).
/// </summary>
/// <param name="DocumentId">Identifiant du document émis.</param>
/// <param name="DocumentNumber">Numéro de document (BT-1), créé par le logiciel source — jamais inventé.</param>
/// <param name="IssueDate">Date d'émission (BT-2).</param>
/// <param name="TotalGross">Montant TTC (BT-112), en <c>decimal</c>.</param>
public sealed record DocumentCandidate(Guid DocumentId, string DocumentNumber, DateOnly IssueDate, decimal TotalGross);
