namespace Liakont.Modules.TenantSettings.Infrastructure;

using Liakont.Modules.TenantSettings.Application;
using Microsoft.AspNetCore.DataProtection;

/// <summary>
/// Implémentation de <see cref="ISecretProtector"/> via ASP.NET Core Data Protection. Les clés de
/// protection sont gérées par l'infrastructure DP de l'instance (persistance par appliance — OPS01) ;
/// ce composant ne fait que protéger/déprotéger sous un « purpose » dédié au module.
/// </summary>
internal sealed class DataProtectionSecretProtector : ISecretProtector
{
    // « Purpose » : isole cryptographiquement les secrets de ce module (versionné pour rotation future).
    private const string Purpose = "Liakont.TenantSettings.PaAccount.ApiKey.v1";

    private readonly IDataProtector _protector;

    public DataProtectionSecretProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector(Purpose);
    }

    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        return _protector.Protect(plaintext);
    }

    public string Unprotect(string protectedValue)
    {
        ArgumentNullException.ThrowIfNull(protectedValue);
        return _protector.Unprotect(protectedValue);
    }
}
