namespace Liakont.Modules.TenantSettings.Domain.Entities;

/// <summary>
/// Statut d'un tenant (F12-A §2). <see cref="Suspendu"/> conserve le paramétrage mais
/// aucune extraction/transmission n'est active (distinct de la fin de vie OPS06, irréversible).
/// </summary>
public enum TenantStatus
{
    Actif = 0,
    Suspendu = 1,
}
