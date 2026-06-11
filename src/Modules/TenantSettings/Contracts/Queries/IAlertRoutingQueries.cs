namespace Liakont.Modules.TenantSettings.Contracts.Queries;

using Liakont.Modules.TenantSettings.Contracts.DTOs;

/// <summary>
/// Lecture de la matrice de routage des alertes d'un tenant (F12 §5.3.1, FIX212). Interface SÉGRÉGÉE
/// (distincte de <see cref="ITenantSettingsQueries"/>) : seul le routage des notifications (module
/// Supervision) et la page de paramétrage la consomment, sans imposer la méthode aux nombreux
/// implémenteurs de <see cref="ITenantSettingsQueries"/>. Scopée par <paramref name="companyId"/>
/// (jamais cross-tenant — CLAUDE.md n°9/17). Liste VIDE = aucune matrice ⇒ modèle simple (défaut).
/// </summary>
public interface IAlertRoutingQueries
{
    Task<IReadOnlyList<AlertRoutingRuleDto>> GetAlertRoutingMatrix(Guid companyId, CancellationToken ct = default);
}
