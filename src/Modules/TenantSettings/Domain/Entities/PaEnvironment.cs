namespace Liakont.Modules.TenantSettings.Domain.Entities;

/// <summary>
/// Environnement d'un compte Plateforme Agréée (F12-A §4). Un tenant peut détenir
/// un compte <see cref="Staging"/> et un compte <see cref="Production"/> pour le même PA.
/// </summary>
public enum PaEnvironment
{
    Staging = 0,
    Production = 1,
}
