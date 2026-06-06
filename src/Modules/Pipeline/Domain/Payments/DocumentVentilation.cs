namespace Liakont.Modules.Pipeline.Domain.Payments;

using System.Collections.Generic;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Pipeline.Domain.Ventilation;

/// <summary>
/// Ventilation par taux d'un document rattaché à un encaissement, telle que résolue depuis le snapshot
/// (ADR-0015) à la version de mapping liée à l'émission. Sert à ventiler un encaissement par taux (PIP03a).
/// </summary>
public sealed record DocumentVentilation
{
    /// <summary>Nature de l'opération du document (PrestationServices déclenche l'e-reporting ; Mixte suspendu ; LivraisonBiens non requis).</summary>
    public required OperationCategory OperationCategory { get; init; }

    /// <summary>Ventilation par taux sourcée (base HT + TVA) issue du mapping validé.</summary>
    public required IReadOnlyList<VentilationLine> Lines { get; init; }
}
