namespace Liakont.Modules.Mandats.Infrastructure;

using Liakont.Modules.Mandats.Application;
using Liakont.Modules.Mandats.Domain.Entities;

/// <summary>
/// Construit les entrées du journal append-only des transitions d'acceptation (INV-ACCEPT-5) à partir de
/// l'agrégat (après transition) et de l'identité de l'opérateur. La persistance atomique (transition +
/// entrée dans la même transaction) est assurée par <c>ISelfBilledAcceptanceUnitOfWork</c>.
/// </summary>
internal static class SelfBilledAcceptanceLogFactory
{
    /// <summary>Entrée de genèse : création de l'agrégat (état initial), <c>from</c> = <c>null</c>.</summary>
    public static SelfBilledAcceptanceLogEntry ForCreation(
        SelfBilledAcceptance acceptance, Guid? operatorId, string? operatorName)
        => new()
        {
            CompanyId = acceptance.CompanyId,
            DocumentId = acceptance.DocumentId,
            FromState = null,
            ToState = acceptance.State,
            OperatorId = operatorId,
            OperatorName = operatorName,
        };

    /// <summary>
    /// Entrée d'une transition d'état : <paramref name="fromState"/> → <c>acceptance.State</c>. <c>operatorId</c>
    /// <c>null</c> = transition système (bascule tacite par job, MND04).
    /// </summary>
    public static SelfBilledAcceptanceLogEntry ForTransition(
        SelfBilledAcceptance acceptance, SelfBilledAcceptanceState fromState, Guid? operatorId, string? operatorName)
        => new()
        {
            CompanyId = acceptance.CompanyId,
            DocumentId = acceptance.DocumentId,
            FromState = fromState,
            ToState = acceptance.State,
            OperatorId = operatorId,
            OperatorName = operatorName,
        };
}
