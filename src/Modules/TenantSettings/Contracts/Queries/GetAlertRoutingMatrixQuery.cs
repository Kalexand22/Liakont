namespace Liakont.Modules.TenantSettings.Contracts.Queries;

using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Stratum.Common.Abstractions.Messaging;

/// <summary>
/// Lit la matrice de routage des alertes du tenant courant (F12 §5.3.1, FIX212), ordonnée par rang.
/// Liste VIDE si le tenant n'a défini aucune entrée (le modèle simple s'applique alors par défaut).
/// </summary>
public record GetAlertRoutingMatrixQuery : IQuery<IReadOnlyList<AlertRoutingRuleDto>>;
