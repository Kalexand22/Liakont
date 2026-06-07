namespace Liakont.Modules.Supervision.Contracts;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Acquittement d'une alerte par l'opérateur d'instance (action du dashboard SUP02). Acquitter ne RÉSOUT
/// pas l'alerte (la résolution est automatique quand la condition disparaît) : c'est un marqueur « prise
/// en charge », journalisé par l'identité de l'opérateur. Tenant-scopé par la connexion.
/// </summary>
public interface IAlertAcknowledgementService
{
    /// <summary>
    /// Acquitte l'alerte <paramref name="alertId"/> au nom de <paramref name="operatorIdentity"/>.
    /// Retourne <c>true</c> si l'alerte existait et a été acquittée, <c>false</c> si elle est absente.
    /// </summary>
    Task<bool> AcknowledgeAsync(Guid alertId, string operatorIdentity, CancellationToken cancellationToken = default);
}
